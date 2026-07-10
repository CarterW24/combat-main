using Sanctuary.Core.IO;

namespace Sanctuary.Packet;

/// <summary>CharacterStatus bitfield (RE'd from the client enum, IDA 2026-07-03). Set on a proxied
/// character via <see cref="PlayerUpdatePacketUpdateCharacterState"/> and stored as
/// ProxiedCharacter::m_nCharacterStatus (offset 0x6F8) by SetCharacterState @0x9613D0. Several bits also
/// halt the character's movement controller (Asleep/Rooted/Stunned/KnockedOut/KnockedBack/Frozen/Scripted).</summary>
[System.Flags]
public enum CharacterStatus
{
    None = 0,
    IsNonAttackable = 0x1,       // real NPCs in the 04-01 capture ship status=1 (non-attackable)
    IsAfraid = 0x2,
    IsAsleep = 0x4,
    IsSilenced = 0x8,
    IsBound = 0x10,
    IsRooted = 0x20,
    IsStunned = 0x40,
    IsKnockedOut = 0x80,
    IsNonAttackable2 = 0x100,
    IsKnockedBack = 0x200,
    IsConfused = 0x2000,
    IsGoingHome = 0x4000,
    IsBoss = 0x8000,             // ProxiedCharacter::IsBoss (bit 15) — flags the actor as a boss
    IsFrozen = 0x10000,
    IsBerserk = 0x20000,
    IsScriptedAnimation = 0x40000,
    IsPoppedUp = 0x100000,
}

// op35 sub-opcode 20 = "UpdateCharacterState". Sets a proxied character's status bitfield
// (ProxiedCharacter::SetCharacterState @0x9613D0 -> m_nCharacterStatus @0x6F8). RE'd 2026-07-03.
// WIRE (2014-04-01 capture idx 28116, len 16): [short 35][short 20][ulong Guid][int Status].
//   Normal NPCs shipped Status=1 (IsNonAttackable). Bosses set IsBoss (0x8000).
public class PlayerUpdatePacketUpdateCharacterState : BasePlayerUpdatePacket, ISerializablePacket
{
    public new const short OpCode = 20;

    public ulong Guid;
    public CharacterStatus Status;

    public PlayerUpdatePacketUpdateCharacterState() : base(OpCode)
    {
    }

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();

        Write(writer); // [op 35][sub 20]

        writer.Write(Guid);
        writer.Write((int)Status);

        return writer.Buffer;
    }
}
