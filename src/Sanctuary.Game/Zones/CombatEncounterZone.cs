using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;

using Sanctuary.Game.Entities;
using Sanctuary.Game.Resources.Definitions.Zones;
using Sanctuary.Packet;
using Sanctuary.Packet.Common;

namespace Sanctuary.Game.Zones;

/// <summary>Shared base for the combat-encounter zones — the generic data-driven <see cref="EncounterArenaZone"/>
/// plus the bespoke <see cref="FrostfangArenaZone"/> and <see cref="TormentedSpiritsArenaZone"/>. It owns the
/// parts every combat encounter shares so a fix lands once instead of three times: the knockout-limit / fail /
/// revive lifecycle. Subclasses supply the encounter id and the zone-specific <see cref="ReturnHome"/> (teardown
/// + teleport), and keep their bespoke bits (Frostfang waves/Alpha, Spirits tombstones) as their own code.
///
/// (First extraction step — the enemy AI, exit door, and win/reward flow still live in the subclasses and are
/// candidates to migrate here next.)</summary>
public abstract class CombatEncounterZone : BaseZone
{
    protected CombatEncounterZone(BaseZoneDefinition zoneDefinition, IServiceProvider serviceProvider)
        : base(zoneDefinition, serviceProvider)
    {
    }

    /// <summary>Knockouts before the encounter fails (retail = 5).</summary>
    protected const int KnockoutLimit = 5;

    private readonly object _knockoutLock = new();
    private readonly Dictionary<ulong, int> _knockouts = [];

    /// <summary>Encounter/activity id + instance for the client encounter packets (respawn window etc.).</summary>
    protected abstract int FailEncounterId { get; }
    protected virtual int FailInstanceId => 1;

    /// <summary>Short label for the knockout log line (e.g. the dungeon name).</summary>
    protected virtual string EncounterLogName => GetType().Name;

    // Combat instances give a long auto-revive FALLBACK — the client's own knockout window runs the real ~10s
    // countdown to the Revive button; this only backstops someone who never presses it.
    protected override int ReviveCooldownMs => 30000;

    /// <summary>Tear the encounter down for this player and teleport them back to the overworld (zone-specific).</summary>
    protected abstract void ReturnHome(Player player);

    /// <summary>Tear the encounter's client UI down for this player (ReturnHome calls this before the teleport,
    /// and the "leave" chat/exit paths call it directly): mark won/lost, remove the minigame state, reset the
    /// encounter data + fighting flags, clear the goals window. On a WIN, GameOver(Won=true) goes FIRST so the
    /// end card the teardown triggers reads as a win; a mid-run bail keeps won=false ("TRY AGAIN!").</summary>
    public void EndEncounterForPlayer(Player player) => EndEncounterForPlayer(player, won: false);

    public void EndEncounterForPlayer(Player player, bool won)
    {
        if (won)
            player.SendTunneled(new MiniGameGameOverPacket(won: true));
        player.SendTunneled(new MiniGameStateRemovePacket());
        player.SendTunneled(PacketEncounterDataCommon.CreateDefault());
        player.SendTunneled(new EncounterOverworldCombatPacket { Unknown3 = false });
        player.SendTunneled(new EncounterPacketIsFighting { InWorldCombat = false });
        player.SendTunneled(new UiObjectiveClearPacket()); // empty + hide the Goals window (op47/sub5)
        _logger.LogInformation("{label}: encounter released for {name}.", EncounterLogName, player.Name);
    }

    /// <summary>Forget a player's knockout tally (call on encounter start/complete so a fresh run starts at 0).</summary>
    protected void ResetKnockouts(ulong guid)
    {
        lock (_knockoutLock)
            _knockouts.Remove(guid);
    }

    /// <summary>How many times this player has been knocked out this run (for the win-screen score).</summary>
    protected int KnockoutsUsed(ulong guid)
    {
        lock (_knockoutLock)
            return _knockouts.TryGetValue(guid, out var k) ? k : 0;
    }

    /// <summary>Enter the encounter at full REAL max HP + mana (Stats[MaxHealth]) so the bar matches the
    /// real-damage claw/bite — a fixed 2500 made it jump on the first hit. Call from OnClientIsReady.</summary>
    protected static void EnterAtFullVitals(Player player)
    {
        var startHp = player.Stats.TryGetValue(CharacterStatId.MaxHealth, out var mh) ? mh.Int : 2500;
        player.CurrentHitpoints = startHp;
        player.SendTunneled(new ClientUpdatePacketHitpoints { CurrentHitpoints = startHp, MaxHitpoints = startHp });
        player.SendTunneled(new ClientUpdatePacketMana { CurrentMana = 100, MaxMana = 100 });
    }

    // The victory exit door (each zone spawns it at its own spot via SpawnExitDoor, then registers it here).
    // Clicking it (routed from CommandPacketInteractRequestHandler) leaves the encounter.
    private readonly object _exitDoorLock = new();
    private Npc? _exitDoor;

    /// <summary>The live victory door, or null. Subclasses read it for the visibility sweep + cleanup.</summary>
    protected Npc? ExitDoor
    {
        get { lock (_exitDoorLock) return _exitDoor; }
    }

    /// <summary>Register the spawned victory door (or null to clear it on a re-run) so IsExitDoor/UseExitDoor
    /// recognise clicks on it.</summary>
    protected void SetExitDoor(Npc? door)
    {
        lock (_exitDoorLock)
            _exitDoor = door;
    }

    public bool IsExitDoor(ulong guid)
    {
        lock (_exitDoorLock)
            return _exitDoor is { } door && door.Guid == guid;
    }

    public void UseExitDoor(Player player)
    {
        _logger.LogInformation("{label}: {name} used the exit door.", EncounterLogName, player.Name);
        ReturnHome(player);
    }

    public override void OnPlayerKnockedOut(Player player)
    {
        if (player.Zone != this)
            return;

        int kos;
        lock (_knockoutLock)
        {
            _knockouts.TryGetValue(player.Guid, out kos);
            kos++;
            _knockouts[player.Guid] = kos;
        }

        _logger.LogInformation("{label}: {name} knocked out ({kos}/{limit}).", EncounterLogName, player.Name, kos, KnockoutLimit);

        // Drop the fighting flags either way (so sub125 shows the auto-recover version, not the overworld
        // pay/safe one).
        player.SendTunneled(new EncounterOverworldCombatPacket { Unknown3 = false });
        player.SendTunneled(new EncounterPacketIsFighting { InWorldCombat = false });

        if (kos >= KnockoutLimit)
        {
            // Out of lives — FAIL. Persistent "TRY AGAIN!" end-screen (SendFailEndScreen: clears the knockdown
            // UI + Won=0 + score card), HOLD it, THEN tear down + teleport home and REVIVE so the player arrives
            // ALIVE (a fail used to strand them knocked out, which blocked firing even through a job-swap).
            SendFailEndScreen(player);
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(FailCardHoldMs);
                    ReturnHome(player);
                    player.Respawn();
                }
                catch (Exception ex) { _logger.LogError(ex, "Fail-return failed."); }
            });
            return;
        }

        // Non-fatal knockout — show the recover window + counter; auto-revive is the fallback.
        player.SendTunneled(new MiniGameKnockOutPacket(kos, KnockoutLimit));
        player.SendTunneled(new EncounterShowRespawnWindowPacket(FailEncounterId, FailInstanceId));
        ScheduleAutoRevive(player);
    }

    public override void OnPlayerRespawn(Player player)
    {
        // Revive with full HP + FX at the death spot (the window's Revive button revives you where you fell).
        var pos = player.DeathPosition;
        player.Respawn();

        if (player.Zone == this)
        {
            player.SendTunneled(new EncounterOverworldCombatPacket { Unknown3 = true });
            player.SendTunneled(new EncounterPacketIsFighting { InWorldCombat = true });
            player.UpdatePosition(pos, player.Rotation);
            player.SendTunneled(new ClientUpdatePacketUpdateLocation
            {
                Position = pos,
                Rotation = player.Rotation,
                Teleport = true,
            });
        }
    }
}
