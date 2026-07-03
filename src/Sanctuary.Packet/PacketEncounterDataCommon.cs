using Sanctuary.Core.IO;

namespace Sanctuary.Packet;

// THE COMBAT GATE (RE'd 2026-07-02 late, user-directed "find what the client expects, don't assume"):
// S2C opcode 62 carries a full EncounterDataCommon combat RULESET. The client-tunnel dispatch
// (OnTunneledClientPacket2 case 62) unserializes it (sub_8F6AE0 -> sub_8D5850) and applies it via
// sub_8D2500, which in ONE shot:
//   - CanUseAbilities/CanUseConsumables  -> shows/hides the action bar
//   - UseCombatCamera + CombatType       -> combat camera ("Camera_ChangeToCombatSettings") + controller
//   - ShowHpBar                          -> m_bIsNpcHitpointBar (NPC health bars, MODE-WIDE — this is why
//                                           arena wolves show bars in the reference video)
//   - ShowCombatUi                       -> combat player/party frames
//   - CombatType                         -> BaseClient::InCombat() == true (blocks job changes etc.)
// BaseClient::InCombat() = EncounterDataCommon.CombatType || (IsFighting && InCombatArea) — so the
// server must push combat rules ON at encounter start and push the DEFAULTS back at exit, or the
// client stays in combat forever (the "can't change jobs after the arena" bug).
//
// Wire format (sub_8D5850, 45 bytes total): [int16 62] + 11 bool bytes + int32 GameTimeOverride +
// float ObjectiveMultiplier + int32 QuickCompletionTime/Value/Loss + int32 NpcKOValue +
// int32 KnockoutRewardValue + int32 KnockoutPenalty. Client ctor defaults: all-false,
// GameTimeOverride=-1, ObjectiveMultiplier=1.0, QuickCompletionTime=3600, rest 0.
public class PacketEncounterDataCommon : ISerializablePacket
{
    public const short OpCode = 62;

    public bool CombatStance;
    public bool CanTakeDamage;
    public bool CanUseAbilities;
    public bool CanUseConsumables;
    public bool CanMelee;
    public bool CanUseTraits;
    public bool CanChangeMaxMoveSpeed;
    public bool ShowHpBar;
    public bool ShowCombatUi;
    public bool UseCombatCamera;
    public bool CombatType;

    public int GameTimeOverride = -1;
    public float ObjectiveMultiplier = 1f;
    public int QuickCompletionTime = 3600;
    public int QuickCompletionValue;
    public int QuickCompletionLoss;
    public int NpcKOValue;
    public int KnockoutRewardValue;
    public int KnockoutPenalty;

    /// <summary>The arena/combat-encounter ruleset (everything on).</summary>
    public static PacketEncounterDataCommon CreateCombatRules() => new()
    {
        CombatStance = true,
        CanTakeDamage = true,
        CanUseAbilities = true,
        CanUseConsumables = true,
        CanMelee = true,
        CanUseTraits = true,
        CanChangeMaxMoveSpeed = true,
        ShowHpBar = true,
        ShowCombatUi = true,
        UseCombatCamera = true,
        CombatType = true,
    };

    /// <summary>The out-of-encounter defaults (matches the client ctor) — releases combat mode.</summary>
    public static PacketEncounterDataCommon CreateDefault() => new();

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();

        writer.Write(OpCode);

        writer.Write(CombatStance);
        writer.Write(CanTakeDamage);
        writer.Write(CanUseAbilities);
        writer.Write(CanUseConsumables);
        writer.Write(CanMelee);
        writer.Write(CanUseTraits);
        writer.Write(CanChangeMaxMoveSpeed);
        writer.Write(ShowHpBar);
        writer.Write(ShowCombatUi);
        writer.Write(UseCombatCamera);
        writer.Write(CombatType);

        writer.Write(GameTimeOverride);
        writer.Write(ObjectiveMultiplier);
        writer.Write(QuickCompletionTime);
        writer.Write(QuickCompletionValue);
        writer.Write(QuickCompletionLoss);
        writer.Write(NpcKOValue);
        writer.Write(KnockoutRewardValue);
        writer.Write(KnockoutPenalty);

        return writer.Buffer;
    }
}
