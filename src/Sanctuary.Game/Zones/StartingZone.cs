using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Sanctuary.Core.Extensions;
using Sanctuary.Core.IO;
using Sanctuary.Game.Combat;
using Sanctuary.Game.Entities;
using Sanctuary.Game.Resources.Definitions.Zones;
using Sanctuary.Packet;
using Sanctuary.Packet.Common;

namespace Sanctuary.Game.Zones;

public sealed class StartingZone : BaseZone
{
    private readonly IZoneManager _zoneManager;
    private readonly IResourceManager _resourceManager;
    private readonly StartingZoneDefinition _zoneDefinition;

    public StartingZone(StartingZoneDefinition zoneDefinition, IServiceProvider serviceProvider)
        : base(zoneDefinition, serviceProvider)
    {
        _zoneDefinition = zoneDefinition;

        _zoneManager = serviceProvider.GetRequiredService<IZoneManager>();
        _resourceManager = serviceProvider.GetRequiredService<IResourceManager>();
    }

    #region Client Is Ready

    public override void OnClientIsReady(Player player)
    {
        SendQuickChatData(player);

        SendPointOfInterests(player);

        SendUpdateStat(player);

        var clientUpdatePacketHitpoints = new ClientUpdatePacketHitpoints
        {
            CurrentHitpoints = 2500,
            MaxHitpoints = 2500
        };

        player.SendTunneled(clientUpdatePacketHitpoints);

        var clientUpdatePacketMana = new ClientUpdatePacketMana
        {
            CurrentMana = 100,
            MaxMana = 100
        };

        player.SendTunneled(clientUpdatePacketMana);

        SendReferenceData(player);

        SendCoinStoreItemList(player);

        SendAdventurersJournalInfo(player);

        // LOGIN ONLY — not on re-zone. This handler runs on EVERY zone-in to the overworld (including
        // the return from the Frostfang arena), and PacketLoadWelcomeScreen makes the client pop the
        // Welcome screen each time it arrives — on the return trip it opened OVER the encounter's
        // victory score screen (user report 2026-07-04). Returning from a battle is a plain re-zone,
        // not a fresh login. (The other reference-data sends above are invisible/idempotent — left
        // alone to keep this change minimal.)
        if (!player.LoginBurstSent)
        {
            player.LoginBurstSent = true;
            SendWelcomeInfo(player);
        }

        SendPlayerCustomizations(player);

        SendMembershipSubscriptionInfo(player);

        SendInGamePurchase(player);

        var packetZoneDoneSendingInitialData = new PacketZoneDoneSendingInitialData();

        player.SendTunneled(packetZoneDoneSendingInitialData);

        var clientUpdatePacketDoneSendingPreloadCharacters = new ClientUpdatePacketDoneSendingPreloadCharacters();

        player.SendTunneled(clientUpdatePacketDoneSendingPreloadCharacters);

        SendFriendList(player);
        SendIgnoreList(player);

        UpdateFriendStatus(player);

        SpawnTrainingDummy(player);

        SpawnGrowlerWolf(player);

        // COMBAT WIP: populate the left ability toolbar on zone load (so we don't have to swap jobs to
        // trigger it). Replays the captured Ninja SetDefinition. Combat/"fighting" state is NOT set here —
        // it's set on the first attack (in the StartAbility handler) so job swaps still work until you swing.
        SendNinjaAbilityToolbar(player);
    }

    // COMBAT WIP: fill the Ninja ability toolbar from the player's EQUIPPED WEAPON (see Combat/
    // NinjaWeaponAbilities). Each "Ninja's Shadow Blade of X" grants the X ability; no Shadow Blade equipped
    // => an empty bar. This is the zone-load populate (so no away-and-back job swap is needed).
    private void SendNinjaAbilityToolbar(Player player)
    {
        if (player.ActiveProfileId != NinjaWeaponAbilities.NinjaProfileId) // Ninja
            return;

        var weaponDefId = player.GetEquippedWeaponDefinitionId();
        var weapon = NinjaWeaponAbilities.GetEquippedWeapon(player);

        _logger.LogInformation(
            "Ninja toolbar on zone-load: equipped weapon def={def}, mapped={mapped} ({melee}/{special}).",
            weaponDefId, weapon is not null, weapon?.Melee.Name ?? "-", weapon?.Special.Name ?? "-");

        player.SendTunneled(NinjaWeaponAbilities.BuildToolbar(player, _resourceManager));
    }

    // COMBAT WIP: spawn a single hostile "training dummy" NPC near the spawn point so we have a
    // target to select + attack while building ability resolution. Pushed directly to the readying
    // player; the tile-visibility system shows it to anyone else nearby. (See docs/STATUS.md.)
    private Npc? _trainingDummy;

    // High HP so the bar visibly drains over many hits instead of dying every ~10 hits and respawning
    // full (which made it look like only the last couple hits registered). Bumped to 50000 because the real
    // ninja ability damage (from the wiki: 2609 melee .. 10674 special) would otherwise one-shot a 5000 dummy.
    private const int TrainingDummyMaxHealth = 50000;

    private void SpawnTrainingDummy(Player player)
    {
        if (_trainingDummy is null)
        {
            if (!TryCreateNpc(out var npc))
                return;

            npc.ModelId = 4;                // robgoblin_m_basic.adr — tagged "Combat NPC" in Models.txt
                                            // (the crab 1667 is a passive critter; may not get a combat
                                            //  health bar). Testing whether a real enemy model fixes it.
            npc.Name = "Training Dummy";
            npc.NameId = 0;
            npc.Disposition = 0;            // 0 = Hostile
            npc.ActiveProfile = 1;          // ★ non-default -> AddNpc apply runs SetProfileId -> color
                                            // resolver re-runs post-disposition -> hostile = RED name
                                            // (user-found 2026-07-03; default 0 skips the resolve)
            npc.Scale = 1f;
            npc.IsInteractable = false;     // no "Press X to talk" prompt — it's a combat target, not an NPC
            npc.Visible = true;
            npc.CursorId = 11;              // cursor_interaction_fight.cur -> crossed-swords attack cursor.
                                            // (was 1 "cursor_interaction_combat" which renders NO cursor in this client)

            // COMBAT WIP: make it damageable + show a health bar so abilities have a visible effect.
            npc.MaxHealth = TrainingDummyMaxHealth;
            npc.Health = TrainingDummyMaxHealth;
            npc.ShowHealthBar = true;

            // A few units off the zone spawn point so it stands in front of the player.
            var pos = new Vector4(SpawnPosition.X + 5f, SpawnPosition.Y, SpawnPosition.Z, SpawnPosition.W);
            npc.UpdatePosition(pos, SpawnRotation);

            _trainingDummy = npc;
        }

        // Make sure this player sees it immediately (don't wait on tile movement).
        player.OnAddVisibleNpcs(_trainingDummy);

        // Mark it attackable (combat cursor) so the client lets the player select it as a target.
        SendNpcRelevance(player, _trainingDummy);

        // RED-NAME TEST (2026-07-03): the AddNpc Disposition int is IGNORED client-side (the apply uses
        // the global arena flag; ctor default = 2 ALLY -> bluish name). op35/sub28 UpdateDisposition is
        // the real per-NPC lever: Disposition 0 HOSTILE -> the color resolver (sub_966460) paints the
        // overhead name RED (0xFFFF0000) as long as no static NameColor is set.
        // (2026-07-03 red-name experiments removed — the dummy's blue name is correct client behavior
        // for a non-arena zone; the nameplate color is resolved once at spawn. See docs/STATUS.md.)

        // Initialize its health bar on the client.
        SendNpcHealth(player, _trainingDummy);
    }

    // INSTANCE WIP (Frostfang Fury, step 1): the "Frostfang Growler" wolf NPC = the adventure-giver. For now
    // (per user) he stands next to the HOME spawn so we can iterate — the icy cave-mouth POI (id 59,
    // 92.81789,66.33743,554.8647) is NOT the video spot; the Sunrise video shows him out in the green grove, so
    // the real overworld location is still TBD. Neutral + interactable (clicking opens the future offer popup).
    private Npc? _growlerWolf;

    private void SpawnGrowlerWolf(Player player)
    {
        if (_growlerWolf is null)
        {
            if (!TryCreateNpc(out var npc))
                return;

            npc.ModelId = 176;              // wolf.adr (basic wolf). Tint/swap to the white "frostfang" look later.
            npc.Name = "Frostfang Growler";
            npc.NameId = 0;
            npc.Disposition = 1;            // Neutral — friendly adventure-giver, NOT a combat target
            npc.Scale = 1f;
            npc.IsInteractable = true;
            npc.Visible = true;
            npc.CursorId = 11;              // cursor_interaction_fight.cur — the crossed-swords FIGHT cursor.
                                            // (cursor 1 "cursor_interaction_combat" renders NOTHING in this client
                                            // — that's why the dummy showed no cursor. 11 is the real swords one.)
                                            // ⚠️ VERIFY ON TEST: if the fight cursor turns the wolf into an attack
                                            // target and breaks click-to-open, fall back to 5 (talk) + the marker.
            // The purple crossed-swords encounter badge ABOVE the head is NOT a nameplate field at
            // all — it's a NOTIFICATION (op35/sub10 AddNotifications -> OverHeadBitmapElement at
            // offset (0,-0.9,0)); see the badge push after OnAddVisibleNpcs below.
            npc.NameplateImageId = 0;
            npc.ImageSetId = 0;
            npc.ShowHealthBar = false;      // MaxHealth stays 0 => not damageable

            // Next to the home spawn (the training dummy sits at X+5; put the wolf on the other side at X-6).
            // +0.6 on Y: the wolf model's origin sits above its feet, so at exact ground Y it half-sinks.
            var pos = new Vector4(SpawnPosition.X - 6f, SpawnPosition.Y + 0.6f, SpawnPosition.Z, SpawnPosition.W);
            npc.UpdatePosition(pos, SpawnRotation);

            _growlerWolf = npc;
        }

        player.OnAddVisibleNpcs(_growlerWolf);

        // Same recipe the training dummy uses to be clickable: tell the client it has a cursor (relevance).
        SendNpcRelevance(player, _growlerWolf);

        SendGrowlerBadge(player);
    }

    // The reference video's crossed-swords badge floating ABOVE the Growler's head. RE'd 2026-07-02:
    // op35/sub10 AddNotifications (byte-exact vs live 2014 pcap) -> client attaches an
    // OverHeadBitmapElement above the character. ImageId 24 in NotificationImages.txt =
    // tint-circle + circle + crossed-swords icon 1345 (the combat-encounter badge art).
    private void SendGrowlerBadge(Player player)
    {
        if (_growlerWolf is null)
            return;

        var badge = new PlayerUpdatePacketAddNotifications();
        badge.Notifications.Add(new NotificationData
        {
            Guid = _growlerWolf.Guid,
            ImageId = 24,
        });

        player.SendTunneled(badge);
    }

    /// <summary>INSTANCE WIP: the Frostfang Growler adventure-giver wolf.</summary>
    public Npc? GrowlerWolf => _growlerWolf;

    /// <summary>Re-push the Growler wolf to a player (e.g. after a "!grove" teleport re-zone).</summary>
    public void ShowGrowlerWolf(Player player)
    {
        if (_growlerWolf is not null)
        {
            player.OnAddVisibleNpcs(_growlerWolf);
            SendGrowlerBadge(player);
        }
    }

    // (SendNpcRelevance / SendNpcHealth moved to BaseZone — shared with the Frostfang arena zone.)

    /// <summary>COMBAT WIP: the live combat target (training dummy).</summary>
    public Npc? TrainingDummy => _trainingDummy;

    // COMBAT WIP: eternal training dummy — instead of despawn/respawn (which stacked extra dummies
    // across relogs), just reset it to full HP and refresh the bar so it's always there to hit.
    public void ResetTrainingDummy()
    {
        var dummy = _trainingDummy;

        if (dummy is null)
            return;

        dummy.Health = dummy.MaxHealth;

        foreach (var zonePlayer in Players)
            SendNpcHealth(zonePlayer, dummy);
    }

    // COMBAT: kill routing for this zone — the only killable NPC here is the eternal training dummy.
    // (The Frostfang encounter wolves live in FrostfangArenaZone, which has its own override.)
    public override void OnNpcKilled(Player killer, Npc npc)
    {
        if (ReferenceEquals(npc, _trainingDummy))
            ResetTrainingDummy();
    }

    // COMBAT WIP: Shadow Army special — spawn temporary "shadow clone" NPCs around the caster, each using the
    // caster's own model, wearing a shadow aura, appearing/vanishing in a puff of black ninja smoke, then
    // despawning after a few seconds. (Customization/outfit copy is a client TODO, so clones are the base body
    // + the shadow aura for now.) FX ids from ActorCompositeEffectDefinitions.xml.
    private const int ShadowCloneModelId = 945;    // human_m_ninja_ghost.adr (Models.txt) — a clothed, ghostly shadow ninja
    private const int ShadowCloneSmokePoof = 21;   // PFX_smoke_black_explosion (ninja appear/vanish poof)
    // Clone AI: run to the enemy, then swing at it on a cooldown (the clones "help you fight").
    private const int CloneTickMs = 300;           // movement/AI tick (client interpolates between updates)
    private const int CloneAttackCooldownMs = 1400;
    private const int CloneAttackAnimation = 1021; // com_1hs_attack_01 — sword swing
    private const int CloneAttackDamage = 200;
    private const int CloneHitFx = 15999;          // PFX_ninja-shadowblade_impact (shadow-blade hit on target)
    private const float CloneMoveSpeed = 9f;       // units/sec toward the target
    private const float CloneAttackRange = 2.5f;   // stop & swing within this distance
    private const int CloneRunAnim = 3;            // loc_run · walk=2 · stand=1 (AnimationGroups.xml)

    public void SummonShadowClones(Player summoner, int count, int lifetimeSeconds)
    {
        // small arc around the caster
        (float dx, float dz)[] offsets = [(-2f, -2f), (2f, -2f), (0f, -3f), (-3f, 1f), (3f, 1f)];

        var clones = new List<Npc>(count);

        for (var i = 0; i < count; i++)
        {
            if (!TryCreateNpc(out var clone))
                break;

            var (dx, dz) = offsets[i % offsets.Length];
            var pos = new Vector4(summoner.Position.X + dx, summoner.Position.Y, summoner.Position.Z + dz, summoner.Position.W);

            clone.ModelId = ShadowCloneModelId; // clothed ghostly shadow ninja (fixes "naked" base body)
            clone.Name = "Shadow Ninja";        // nameplate text (matches the real ability)
            clone.NameId = 0;
            clone.HideNamePlate = false;        // show the "Shadow Ninja" nameplate
            clone.Disposition = 2;              // Ally (your shadow ninjas)
            clone.Scale = 1f;
            clone.IsInteractable = false;
            clone.CursorId = 0;
            clone.CompositeEffectId = 0;        // ghost model is already shadowy; NO persistent (_loop) aura -> nothing lingers
            clone.RunAnimId = CloneRunAnim;     // play the run clip while moving to the enemy
            clone.WalkAnimId = 2;               // loc_walk
            clone.StandAnimId = 1;              // loc_stand
            clone.Visible = true;
            clone.UpdatePosition(pos, summoner.Rotation);

            summoner.OnAddVisibleNpcs(clone);   // make it appear for the caster
            clone.OnAddVisiblePlayers(summoner); // track the caster so Dispose() removes it from their client

            // ninja smoke poof at the spawn spot
            summoner.SendTunneled(new PlayerUpdatePacketPlayCompositeEffect
            {
                Guid = clone.Guid,
                CompositeEffectId = ShadowCloneSmokePoof,
                Position = pos,
            });

            clones.Add(clone);
        }

        if (clones.Count == 0)
            return;

        _logger.LogInformation("Shadow Army: summoned {n} clones for {sec}s (model {model}).",
            clones.Count, lifetimeSeconds, summoner.Model);

        // despawn after the lifetime (off-thread, mirrors the damage-resolve pattern)
        _ = Task.Run(async () =>
        {
            try
            {
                // CLONE AI: run to the dummy (re-targeting its position each tick = chase), then swing on a
                // cooldown once in range. Position updates each tick; the client interpolates -> smooth run.
                var totalMs = lifetimeSeconds * 1000;
                var nextAttackMs = new int[clones.Count]; // per-clone next-attack time (ms since start)

                for (var elapsed = 0; elapsed < totalMs; elapsed += CloneTickMs)
                {
                    await Task.Delay(CloneTickMs);

                    var dummy = _trainingDummy;
                    if (dummy is null)
                        continue;

                    var target = new Vector3(dummy.Position.X, dummy.Position.Y, dummy.Position.Z);

                    for (var i = 0; i < clones.Count; i++)
                    {
                        var clone = clones[i];
                        var here = new Vector3(clone.Position.X, clone.Position.Y, clone.Position.Z);
                        var toTarget = target - here;
                        var dist = toTarget.Length();

                        // face the dummy (yaw about Y)
                        var yaw = (float)Math.Atan2(toTarget.X, toTarget.Z);
                        var rot = Quaternion.CreateFromYawPitchRoll(yaw, 0f, 0f);

                        if (dist > CloneAttackRange)
                        {
                            // step toward the dummy
                            var step = Math.Min(CloneMoveSpeed * (CloneTickMs / 1000f), dist - CloneAttackRange);
                            var dir = toTarget / dist;
                            var np = here + dir * step;
                            var newPos = new Vector4(np.X, np.Y, np.Z, clone.Position.W);

                            clone.UpdatePosition(newPos, rot);
                            summoner.SendTunneled(new PlayerUpdatePacketUpdatePosition
                            {
                                Guid = clone.Guid, Position = newPos, Rotation = rot, State = 1, Unknown = 0,
                            });
                        }
                        else
                        {
                            // in range: hold, face the dummy, swing on cooldown
                            clone.UpdatePosition(clone.Position, rot);
                            summoner.SendTunneled(new PlayerUpdatePacketUpdatePosition
                            {
                                Guid = clone.Guid, Position = clone.Position, Rotation = rot, State = 0, Unknown = 0,
                            });

                            if (elapsed >= nextAttackMs[i] && dummy.IsAlive)
                            {
                                nextAttackMs[i] = elapsed + CloneAttackCooldownMs;

                                // swing (StartCasting animates the clone's guid)
                                summoner.SendTunneled(new AbilityPacketStartCasting
                                {
                                    Unknown = clone.Guid, Unknown2 = dummy.Guid, CompositeEffectId = 0,
                                    Animation = CloneAttackAnimation, AbilityId = 0, ActionTime = 0.3f, HasActionProgress = false,
                                });

                                // damage + shadow-blade hit on the dummy
                                var killed = dummy.ApplyDamage(CloneAttackDamage);
                                summoner.SendTunneled(new CombatPacketAttackProcessed
                                {
                                    AttackerGuid = clone.Guid,
                                    TargetGuid = dummy.Guid,
                                    Damage = CloneAttackDamage,
                                    MaxHealth = dummy.MaxHealth,
                                    CompositeEffectId = CloneHitFx,
                                    CurrentHealth = dummy.Health,
                                });

                                if (killed)
                                    ResetTrainingDummy();
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Shadow Army clone AI failed.");
            }
            finally
            {
                // poof out + remove every clone
                foreach (var clone in clones)
                {
                    summoner.SendTunneled(new PlayerUpdatePacketPlayCompositeEffect
                    {
                        Guid = clone.Guid,
                        CompositeEffectId = ShadowCloneSmokePoof,
                        Position = clone.Position,
                    });

                    clone.Dispose(); // RemovePlayer to the caster + clears zone tile + zone registration
                }
            }
        });
    }


    private void SendQuickChatData(Player player)
    {
        var quickChatSendDataPacket = new QuickChatSendDataPacket();

        quickChatSendDataPacket.QuickChats = _resourceManager.QuickChats.ToDictionary();

        player.SendTunneled(quickChatSendDataPacket);
    }

    private void SendPointOfInterests(Player player)
    {
        var packetPointOfInterestDefinitionReply = new PacketPointOfInterestDefinitionReply();
        using var writer = new PacketWriter();

        foreach (var pointOfInterest in _resourceManager.PointOfInterests.Values)
        {
            writer.Write(true);

            pointOfInterest.Serialize(writer);
        }

        writer.Write(false);

        packetPointOfInterestDefinitionReply.Payload = writer.Buffer;

        player.SendTunneled(packetPointOfInterestDefinitionReply);
    }

    private void SendUpdateStat(Player player)
    {
        var clientUpdatePacketUpdateStat = new ClientUpdatePacketUpdateStat();

        clientUpdatePacketUpdateStat.Guid = player.Guid;

        // TODO
        clientUpdatePacketUpdateStat.Stats.AddRange(
        [
            new CharacterStat(CharacterStatId.MaxHealth, 2500),
            new CharacterStat(CharacterStatId.MaxMovementSpeed, 8f),
            new CharacterStat(CharacterStatId.WeaponRange, 5f),
            new CharacterStat(CharacterStatId.HitPointRegen, 25),
            new CharacterStat(CharacterStatId.MaxMana, 100),
            new CharacterStat(CharacterStatId.ManaRegen, 4),
            new CharacterStat(CharacterStatId.MeleeChanceToHit, 100),
            new CharacterStat(CharacterStatId.MeleeWeaponDamageMultiplier, 1f),
            new CharacterStat(CharacterStatId.MeleeHandToHandDamage, 1),
            new CharacterStat(CharacterStatId.EquippedMeleeWeaponDamage, 1),
            new CharacterStat(CharacterStatId.MeleeAttackIntervalMs, 2000),
            new CharacterStat(CharacterStatId.DamageMultiplier, 1f),
            new CharacterStat(CharacterStatId.HealingMultiplier, 1f),
            new CharacterStat(CharacterStatId.AbilityCriticalHitMultiplier, 1f),
            new CharacterStat(CharacterStatId.HeadInflationPercent, 100),
            new CharacterStat(CharacterStatId.RangeMultiplier, 1f),
            new CharacterStat(CharacterStatId.FactoryProductionModifier, 1f),
            new CharacterStat(CharacterStatId.FactoryYieldModifier, 1f),
            new CharacterStat(CharacterStatId.InCombatHitPointRegen, 6),
            new CharacterStat(CharacterStatId.InCombatManaRegen, 4)
        ]);

        player.SendTunneled(clientUpdatePacketUpdateStat);
    }

    private void SendReferenceData(Player player)
    {
        var referenceDataPacketItemClassDefinitions = new ReferenceDataPacketItemClassDefinitions();

        referenceDataPacketItemClassDefinitions.ItemClasses = _resourceManager.ItemClasses.ToDictionary();

        player.SendTunneled(referenceDataPacketItemClassDefinitions);

        var referenceDataPacketItemCategoryDefinitions = new ReferenceDataPacketItemCategoryDefinitions();

        referenceDataPacketItemCategoryDefinitions.ItemCategories = _resourceManager.ItemCategories.ToDictionary();
        referenceDataPacketItemCategoryDefinitions.ItemCategoryGroups = _resourceManager.ItemCategoryGroups.ToDictionary();

        player.SendTunneled(referenceDataPacketItemCategoryDefinitions);

        var referenceDataPacketClientProfileData = new ReferenceDataPacketClientProfileData();

        referenceDataPacketClientProfileData.Profiles = _resourceManager.Profiles.ToDictionary();

        player.SendTunneled(referenceDataPacketClientProfileData);
    }

    private void SendCoinStoreItemList(Player player)
    {
        var coinStoreItemListPacket = new CoinStoreItemListPacket();

        coinStoreItemListPacket.StaticItems = _resourceManager.CoinStoreItems.ToDictionary();

        player.SendTunneled(coinStoreItemListPacket);

        var clientItemDefinitions = new List<ClientItemDefinition>();

        foreach (var coinStoreItem in _resourceManager.CoinStoreItems)
        {
            if (!_resourceManager.ClientItemDefinitions.TryGetValue(coinStoreItem.Key, out var clientItemDefinition))
                continue;

            clientItemDefinitions.Add(clientItemDefinition);
        }

        using var writer = new PacketWriter();

        writer.Write(clientItemDefinitions);

        var playerUpdatePacketItemDefinitions = new PlayerUpdatePacketItemDefinitions();

        playerUpdatePacketItemDefinitions.Payload = writer.Buffer;

        player.SendTunneled(playerUpdatePacketItemDefinitions);
    }

    private void SendAdventurersJournalInfo(Player player)
    {
        // DO NOT REMOVE even if it's not fully implemented. This packet is needed
        // due to an Area Definition called "Newbiezone" in FabledRealmsAreas.xml.

        var adventurersJournal = new AdventurersJournalInfoPacket();

        AdventurersJournalRegionDefinition[] regions =
        [
            new()
            {
                Id = 1,
                NameId = 5100069,
                DescriptionId = 5100031,
                TabImageId = 35449,
                ChapterMapImageId = 0,
                GeometryId = 244,
                CompletedStringId = 5101408
            },
            new()
            {
                Id = 2,
                NameId = 442123,
                DescriptionId = 5100032,
                TabImageId = 9532,
                ChapterMapImageId = 0,
                GeometryId = 5,
                CompletedStringId = 442681,
            },
            new()
            {
                Id = 3,
                NameId = 3501,
                DescriptionId = 2129,
                TabImageId = 9538,
                ChapterMapImageId = 0,
                GeometryId = 8,
                CompletedStringId = 5101409,
            },
            new()
            {
                Id = 4,
                NameId = 3505,
                DescriptionId = 442685,
                TabImageId = 9529,
                ChapterMapImageId = 0,
                GeometryId = 1,
                CompletedStringId = 442686,
            }
        ];

        adventurersJournal.Regions = regions.ToDictionary(x => x.Id);

        AdventurersJournalHubDefinition[] hubs =
        [
            new()
            {
                Id = 1,
                RegionId = 1,
                DisplayOrder = 1,
                NameId = 442216,
                ActiveImageSetId = 19,
                ImageSetId = 44308,
                CompletedImageSetId = 44310,
                CompletedDescriptionId = 5100071,
                MapX = 0,
                MapY = 0
            },
            new()
            {
                Id = 2,
                RegionId = 1,
                DisplayOrder = 2,
                NameId = 18735,
                ActiveImageSetId = 19,
                ImageSetId = 44308,
                CompletedImageSetId = 44311,
                CompletedDescriptionId = 5100072,
                MapX = 0,
                MapY = 0
            },
            new()
            {
                Id = 3,
                RegionId = 1,
                DisplayOrder = 3,
                NameId = 5100069,
                ActiveImageSetId = 19,
                ImageSetId = 44308,
                CompletedImageSetId = 44309,
                CompletedDescriptionId = 5100073,
                MapX = 0,
                MapY = 0
            },
            new()
            {
                Id = 4,
                RegionId = 2,
                DisplayOrder = 1,
                NameId = 7262,
                ActiveImageSetId = 19,
                ImageSetId = 44308,
                CompletedImageSetId = 44941,
                CompletedDescriptionId = 442125,
                MapX = 0,
                MapY = 0
            },
            new()
            {
                Id = 5,
                RegionId = 2,
                DisplayOrder = 2,
                NameId = 428995,
                ActiveImageSetId = 19,
                ImageSetId = 44308,
                CompletedImageSetId = 44942,
                CompletedDescriptionId = 442126,
                MapX = 0,
                MapY = 0
            },
            new()
            {
                Id = 6,
                RegionId = 2,
                DisplayOrder = 3,
                NameId = 442124,
                ActiveImageSetId = 19,
                ImageSetId = 44308,
                CompletedImageSetId = 44945,
                CompletedDescriptionId = 442127,
                MapX = 0,
                MapY = 0
            },
            new()
            {
                Id = 7,
                RegionId = 2,
                DisplayOrder = 4,
                NameId = 4428,
                ActiveImageSetId = 19,
                ImageSetId = 44308,
                CompletedImageSetId = 44943,
                CompletedDescriptionId = 442128,
                MapX = 0,
                MapY = 0
            },
            new()
            {
                Id = 8,
                RegionId = 3,
                DisplayOrder = 1,
                NameId = 5101823,
                ActiveImageSetId = 19,
                ImageSetId = 44308,
                CompletedImageSetId = 45267,
                CompletedDescriptionId = 5101824,
                MapX = 0,
                MapY = 0
            },
            new()
            {
                Id = 9,
                RegionId = 3,
                DisplayOrder = 2,
                NameId = 5101825,
                ActiveImageSetId = 19,
                ImageSetId = 44308,
                CompletedImageSetId = 45268,
                CompletedDescriptionId = 5101826,
                MapX = 0,
                MapY = 0
            },
            new()
            {
                Id = 10,
                RegionId = 4,
                DisplayOrder = 1,
                NameId = 442623,
                ActiveImageSetId = 19,
                ImageSetId = 44308,
                CompletedImageSetId = 45600,
                CompletedDescriptionId = 442687,
                MapX = 0,
                MapY = 0
            }
        ];

        adventurersJournal.Hubs = hubs.ToDictionary(x => x.Id);

        AdventurersJournalHubQuestDefinition[] hubQuests =
        [
            new()
            {
                HubId = 1,
                Id = 2514,
                Unknown = 2
            },
            new()
            {
                HubId = 1,
                Id = 2513,
                Unknown = 1
            },
            new()
            {
                HubId = 2,
                Id = 2521,
                Unknown = 2
            },
            new()
            {
                HubId = 2,
                Id = 2526,
                Unknown = 7
            },
            new()
            {
                HubId = 2,
                Id = 2522,
                Unknown = 3
            },
            new()
            {
                HubId = 2,
                Id = 2523,
                Unknown = 4
            },
            new()
            {
                HubId = 2,
                Id = 2524,
                Unknown = 5
            },
            new()
            {
                HubId = 2,
                Id = 2525,
                Unknown = 6
            },
            new()
            {
                HubId = 3,
                Id = 2529,
                Unknown = 3
            },
            new()
            {
                HubId = 3,
                Id = 2528,
                Unknown = 2
            },
            new()
            {
                HubId = 3,
                Id = 2527,
                Unknown = 1
            },
            new()
            {
                HubId = 3,
                Id = 2566,
                Unknown = 5
            },
            new()
            {
                HubId = 3,
                Id = 2530,
                Unknown = 4
            },
            new()
            {
                HubId = 4,
                Id = 2493,
                Unknown = 6
            },
            new()
            {
                HubId = 4,
                Id = 2492,
                Unknown = 5
            },
            new()
            {
                HubId = 4,
                Id = 2491,
                Unknown = 4
            },
            new()
            {
                HubId = 4,
                Id = 2490,
                Unknown = 3
            },
            new()
            {
                HubId = 4,
                Id = 2489,
                Unknown = 2
            },
            new()
            {
                HubId = 4,
                Id = 2538,
                Unknown = 1
            },
            new()
            {
                HubId = 5,
                Id = 2498,
                Unknown = 6
            },
            new()
            {
                HubId = 5,
                Id = 2497,
                Unknown = 5
            },
            new()
            {
                HubId = 5,
                Id = 2496,
                Unknown = 4
            },
            new()
            {
                HubId = 5,
                Id = 2495,
                Unknown = 3
            },
            new()
            {
                HubId = 5,
                Id = 2494,
                Unknown = 2
            },
            new()
            {
                HubId = 5,
                Id = 2531,
                Unknown = 1
            },
            new()
            {
                HubId = 6,
                Id = 2502,
                Unknown = 4
            },
            new()
            {
                HubId = 6,
                Id = 2501,
                Unknown = 3
            },
            new()
            {
                HubId = 6,
                Id = 2500,
                Unknown = 2
            },
            new()
            {
                HubId = 6,
                Id = 2499,
                Unknown = 1
            },
            new()
            {
                HubId = 6,
                Id = 2503,
                Unknown = 5
            },
            new()
            {
                HubId = 7,
                Id = 2533,
                Unknown = 7
            },
            new()
            {
                HubId = 7,
                Id = 2532,
                Unknown = 1
            },
            new()
            {
                HubId = 7,
                Id = 2504,
                Unknown = 2
            },
            new()
            {
                HubId = 7,
                Id = 2508,
                Unknown = 6
            },
            new()
            {
                HubId = 7,
                Id = 2507,
                Unknown = 5
            },
            new()
            {
                HubId = 7,
                Id = 2505,
                Unknown = 3
            },
            new()
            {
                HubId = 7,
                Id = 2506,
                Unknown = 4
            },
            new()
            {
                HubId = 8,
                Id = 2580,
                Unknown = 5
            },
            new()
            {
                HubId = 8,
                Id = 2578,
                Unknown = 3
            },
            new()
            {
                HubId = 8,
                Id = 2579,
                Unknown = 4
            },
            new()
            {
                HubId = 8,
                Id = 2577,
                Unknown = 2
            },
            new()
            {
                HubId = 8,
                Id = 2576,
                Unknown = 1
            },
            new()
            {
                HubId = 9,
                Id = 2585,
                Unknown = 10
            },
            new()
            {
                HubId = 9,
                Id = 2584,
                Unknown = 9
            },
            new()
            {
                HubId = 9,
                Id = 2583,
                Unknown = 8
            },
            new()
            {
                HubId = 9,
                Id = 2582,
                Unknown = 7
            },
            new()
            {
                HubId = 9,
                Id = 2581,
                Unknown = 6
            },
            new()
            {
                HubId = 9,
                Id = 2600,
                Unknown = 11
            },
            new()
            {
                HubId = 10,
                Id = 2595,
                Unknown = 6
            },
            new()
            {
                HubId = 10,
                Id = 2594,
                Unknown = 5
            },
            new()
            {
                HubId = 10,
                Id = 2591,
                Unknown = 4
            },
            new()
            {
                HubId = 10,
                Id = 2590,
                Unknown = 3
            },
            new()
            {
                HubId = 10,
                Id = 2596,
                Unknown = 7
            },
            new()
            {
                HubId = 10,
                Id = 2588,
                Unknown = 1
            },
            new()
            {
                HubId = 10,
                Id = 2599,
                Unknown = 10
            },
            new()
            {
                HubId = 10,
                Id = 2598,
                Unknown = 9
            },
            new()
            {
                HubId = 10,
                Id = 2597,
                Unknown = 8
            },
            new()
            {
                HubId = 10,
                Id = 2589,
                Unknown = 2
            }
        ];

        adventurersJournal.HubQuests = hubQuests.ToDictionary(x => x.Id);

        AdventurersJournalStickerDefinition[] stickers =
        [
            new()
            {
                Id = 1,
                RegionId = 1,
                DisplayOrder = 1,
                QuestId = 2563,
                NameId = 5100479,
                DescriptionId = 5100480,
                CompletedImageSetId = 43279,
                ImageSetId = 43278,
                Unknown = 0
            },
            new()
            {
                Id = 2,
                RegionId = 1,
                DisplayOrder = 2,
                QuestId = 2564,
                NameId = 5100483,
                DescriptionId = 5100484,
                CompletedImageSetId = 43287,
                ImageSetId = 43286,
                Unknown = 0
            },
            new()
            {
                Id = 3,
                RegionId = 1,
                DisplayOrder = 3,
                QuestId = 2565,
                NameId = 5100487,
                DescriptionId = 5100488,
                CompletedImageSetId = 43273,
                ImageSetId = 43272,
                Unknown = 0
            },
            new()
            {
                Id = 4,
                RegionId = 1,
                DisplayOrder = 4,
                QuestId = 2572,
                NameId = 5100772,
                DescriptionId = 5100773,
                CompletedImageSetId = 43281,
                ImageSetId = 43280,
                Unknown = 0
            },
            new()
            {
                Id = 5,
                RegionId = 1,
                DisplayOrder = 5,
                QuestId = 2573,
                NameId = 5100776,
                DescriptionId = 5100777,
                CompletedImageSetId = 43291,
                ImageSetId = 43290,
                Unknown = 0
            },
            new()
            {
                Id = 6,
                RegionId = 1,
                DisplayOrder = 6,
                QuestId = 2587,
                NameId = 5101187,
                DescriptionId = 5101188,
                CompletedImageSetId = 43283,
                ImageSetId = 43282,
                Unknown = 0
            },
            new()
            {
                Id = 16,
                RegionId = 2,
                DisplayOrder = 1,
                QuestId = 2568,
                NameId = 5100756,
                DescriptionId = 5100757,
                CompletedImageSetId = 43305,
                ImageSetId = 43304,
                Unknown = 0
            },
            new()
            {
                Id = 17,
                RegionId = 2,
                DisplayOrder = 2,
                QuestId = 2569,
                NameId = 5100760,
                DescriptionId = 5100761,
                CompletedImageSetId = 43287,
                ImageSetId = 43286,
                Unknown = 0
            },
            new()
            {
                Id = 18,
                RegionId = 2,
                DisplayOrder = 3,
                QuestId = 2570,
                NameId = 5100764,
                DescriptionId = 5100765,
                CompletedImageSetId = 43273,
                ImageSetId = 43272,
                Unknown = 0
            },
            new()
            {
                Id = 19,
                RegionId = 2,
                DisplayOrder = 4,
                QuestId = 2571,
                NameId = 5100768,
                DescriptionId = 5100769,
                CompletedImageSetId = 43279,
                ImageSetId = 43278,
                Unknown = 0
            },
            new()
            {
                Id = 20,
                RegionId = 2,
                DisplayOrder = 5,
                QuestId = 2574,
                NameId = 5100780,
                DescriptionId = 5100781,
                CompletedImageSetId = 43277,
                ImageSetId = 43276,
                Unknown = 0
            },
            new()
            {
                Id = 21,
                RegionId = 2,
                DisplayOrder = 6,
                QuestId = 2575,
                NameId = 5100784,
                DescriptionId = 5100785,
                CompletedImageSetId = 43283,
                ImageSetId = 43282,
                Unknown = 0
            },
            new()
            {
                Id = 32,
                RegionId = 3,
                DisplayOrder = 2,
                QuestId = 2602,
                NameId = 442851,
                DescriptionId = 442857,
                CompletedImageSetId = 43287,
                ImageSetId = 43286,
                Unknown = 0
            },
            new()
            {
                Id = 35,
                RegionId = 3,
                DisplayOrder = 5,
                QuestId = 2605,
                NameId = 442854,
                DescriptionId = 442860,
                CompletedImageSetId = 43279,
                ImageSetId = 43278,
                Unknown = 0
            },
            new()
            {
                Id = 36,
                RegionId = 3,
                DisplayOrder = 6,
                QuestId = 2606,
                NameId = 442855,
                DescriptionId = 442861,
                CompletedImageSetId = 43305,
                ImageSetId = 43304,
                Unknown = 0
            },
            new()
            {
                Id = 37,
                RegionId = 4,
                DisplayOrder = 1,
                QuestId = 2592,
                NameId = 0,
                DescriptionId = 0,
                CompletedImageSetId = 0,
                ImageSetId = 0,
                Unknown = 0
            }
        ];

        adventurersJournal.Stickers = stickers.ToDictionary(x => x.Id);

        player.SendTunneled(adventurersJournal);
    }

    private void SendWelcomeInfo(Player player)
    {
        var packetLoadWelcomeScreen = new PacketLoadWelcomeScreen();

        packetLoadWelcomeScreen.Contents.AddRange(
        [
            new ContentInfo
            {
                NameId = 6185,
                DescriptionId = 6186,
            },
            new ContentInfo
            {
                NameId = 6187,
                DescriptionId = 6188,
            },
            new ContentInfo
            {
                NameId = 6189,
                DescriptionId = 6190,
            }
        ]);

        packetLoadWelcomeScreen.ClaimCodes.AddRange(
        [
            new ClaimCodeInfo
            {
                Code = "MMMDONUT",
                NameId = 401519,
                DescriptionId = 401534,
                IconId = 929
            },
            new ClaimCodeInfo
            {
                Code = "BERRYCUPCAKE",
                NameId = 401517,
                DescriptionId = 401532,
                IconId = 939
            },
            new ClaimCodeInfo
            {
                Code = "SKELETAL",
                NameId = 409157,
                DescriptionId = 109132,
                IconId = 3459
            },
            new ClaimCodeInfo
            {
                Code = "STRAWBERRIES",
                NameId = 409158,
                DescriptionId = 108948,
                IconId = 3441
            },
            new ClaimCodeInfo
            {
                Code = "FROGGY",
                NameId = 409159,
                DescriptionId = 3141,
                IconId = 1258
            },
            new ClaimCodeInfo
            {
                Code = "SANDWICH",
                NameId = 409160,
                DescriptionId = 2430,
                IconId = 949
            }
        ]);

        player.SendTunneled(packetLoadWelcomeScreen);
    }

    private void SendPlayerCustomizations(Player player)
    {
        var playerUpdatePacketCustomizationData = new PlayerUpdatePacketCustomizationData();

        var customizations = new[]
        {
            new PlayerCustomizationData
            {
                Id = 0, // Head
                Param = player.HeadId,
                StringParam = player.Head
            },
            new PlayerCustomizationData
            {
                Id = 1, // Skin Tone
                Param = player.SkinToneId,
                StringParam = player.SkinTone
            },
            new PlayerCustomizationData
            {
                Id = 2, // Hair
                Param = player.HairId,
                StringParam = player.Hair
            },
            new PlayerCustomizationData
            {
                Id = 3, // Hair Color
                Param = player.HairColor
            },
            new PlayerCustomizationData
            {
                Id = 4, // Eye Color
                Param = player.EyeColor
            },
            new PlayerCustomizationData
            {
                Id = 5, // Model Customization
                Param = player.ModelCustomizationId,
                StringParam = player.ModelCustomization
            },
            new PlayerCustomizationData
            {
                Id = 6, // Face Paint
                Param = player.FacePaintId,
                StringParam = player.FacePaint
            },
            new PlayerCustomizationData
            {
                Id = 8, // Model
                Param = player.Model
            }
        };

        playerUpdatePacketCustomizationData.Customizations.AddRange(customizations);

        player.SendTunneled(playerUpdatePacketCustomizationData);
    }

    private void SendMembershipSubscriptionInfo(Player player)
    {
        var packetMembershipSubscriptionInfo = new PacketMembershipSubscriptionInfo
        {
            IsMember = player.MembershipStatus != 0
        };

        player.SendTunneled(packetMembershipSubscriptionInfo);
    }

    private void SendInGamePurchase(Player player)
    {
        var packetInGamePurchaseEnableMarketplace = new PacketInGamePurchaseEnableMarketplace
        {
            Enabled = true
        };

        player.SendTunneled(packetInGamePurchaseEnableMarketplace);

        var packetInGamePurchaseStoreEnablePaymentSources = new PacketInGamePurchaseStoreEnablePaymentSources
        {
            Sms = true,
            Paypal = true
        };

        player.SendTunneled(packetInGamePurchaseStoreEnablePaymentSources);

        var packetInGamePurchaseStoreBundleCategoryGroups = new PacketInGamePurchaseStoreBundleCategoryGroups();

        packetInGamePurchaseStoreBundleCategoryGroups.CategoryGroups = _resourceManager.StoreBundleCategoryGroups.ToDictionary();

        player.SendTunneled(packetInGamePurchaseStoreBundleCategoryGroups);

        var packetInGamePurchaseStoreBundleCategories = new PacketInGamePurchaseStoreBundleCategories();

        packetInGamePurchaseStoreBundleCategories.CategoryTree.Categories = _resourceManager.StoreBundleCategories.ToDictionary();

        player.SendTunneled(packetInGamePurchaseStoreBundleCategories);

        if (_resourceManager.Stores.TryGetValue(1, out var mainStore))
        {
            var packetInGamePurchaseStoreBundles = new PacketInGamePurchaseStoreBundles();

            packetInGamePurchaseStoreBundles.StoreId = mainStore.Id;

            packetInGamePurchaseStoreBundles.Store.Id = mainStore.Id;
            packetInGamePurchaseStoreBundles.Store.NameId = mainStore.NameId;
            packetInGamePurchaseStoreBundles.Store.DescriptionId = mainStore.DescriptionId;
            packetInGamePurchaseStoreBundles.Store.Image = mainStore.Image;

            foreach (var storeBundle in mainStore.Bundles.Values)
            {
                var valid = storeBundle.Entries.All(x => _resourceManager.ClientItemDefinitions.ContainsKey(x.MarketingItemId));

                if (valid)
                    packetInGamePurchaseStoreBundles.Store.Bundles.Add(storeBundle.Id, storeBundle);
            }

            player.SendTunneled(packetInGamePurchaseStoreBundles);
        }

        var packetInGamePurchaseStoreBundleGroups = new PacketInGamePurchaseStoreBundleGroups();

        packetInGamePurchaseStoreBundleGroups.BundleGroups = _resourceManager.StoreBundleGroups.ToDictionary();

        player.SendTunneled(packetInGamePurchaseStoreBundleGroups);

        /* var inGamePurchaseUpdateSaleDisplay = new InGamePurchaseUpdateSaleDisplay();

        inGamePurchaseUpdateSaleDisplay.Sales.Add(new SaleDisplayInfo
        {
            Id = 12951,
            IconId = 7866,
            TintId = 0,
            TitleId = 824,
            BodyId = 825,
            SecondsLeft = 1000,
            Unknown = 0,
            IsMembership = false
        });

        player.SendTunneled(inGamePurchaseUpdateSaleDisplay); */
    }

    private void SendFriendList(Player player)
    {
        var friendListPacket = new FriendListPacket();

        friendListPacket.Friends = player.Friends;

        player.SendTunneled(friendListPacket);
    }

    private void SendIgnoreList(Player player)
    {
        var ignoreListPacket = new IgnoreListPacket();

        ignoreListPacket.Ignores = player.Ignores;

        player.SendTunneled(ignoreListPacket);
    }

    private void UpdateFriendStatus(Player player)
    {
        var friendOnlinePacket = new FriendOnlinePacket();

        friendOnlinePacket.Guid = player.Guid;

        friendOnlinePacket.IsLocal = true;

        var friendStatusPacket = new FriendStatusPacket
        {
            Guid = player.Guid,
            Status =
            {
                ProfileId = player.ActiveProfile.Id,
                ProfileRank = player.ActiveProfile.Rank,
                ProfileIconId = player.ActiveProfile.Icon,
                ProfileNameId = player.ActiveProfile.NameId,
                ProfileBackgroundImageId = player.ActiveProfile.BadgeImageSet
            }
        };

        foreach (var friend in player.Friends)
        {
            if (!_zoneManager.TryGetPlayer(friend.Guid, out var friendPlayer))
                continue;

            var otherFriendPlayer = friendPlayer.Friends.FirstOrDefault(x => x.Guid == player.Guid);

            if (otherFriendPlayer is null || otherFriendPlayer.Online)
                continue;

            otherFriendPlayer.Online = true;

            friendPlayer.SendTunneled(friendOnlinePacket);
            friendPlayer.SendTunneled(friendStatusPacket);
        }
    }

    #endregion

    public int GetZoneAreaId(Vector4 position)
    {
        foreach (var areaDefinition in _zoneDefinition.AreaDefinitions)
        {
            if (areaDefinition.Shape == "Circle")
            {
                var circle = new Vector3(areaDefinition.X1, 0, areaDefinition.Z1);

                if (position.IsInCircle(circle, areaDefinition.Radius))
                    return areaDefinition.Id;
            }
            else if (areaDefinition.Shape == "Rectangle")
            {
                var p1 = new Vector3(areaDefinition.X1, 0, areaDefinition.Z1);
                var p2 = new Vector3(areaDefinition.X2, 0, areaDefinition.Z2);

                if (position.IsInRectangle(p1, p2))
                    return areaDefinition.Id;
            }
            else
            {
                throw new NotImplementedException(nameof(areaDefinition.Shape));
            }
        }

        return 0;
    }
}
