using System.Collections.Generic;
using System.Numerics;

using Sanctuary.Core.IO;
using Sanctuary.Packet.Common;
using Sanctuary.Packet.Common.Targets;

namespace Sanctuary.Packet;

// Server -> client: the full ability-cast projectile packet (op36 sub 4). Retail's "press an ability
// that shoots" message — caster/target context + the projectile itself in one packet.
//
// Field order VERIFIED against the client reader sub_A31F30 (IDA): it reads the fields below and ends
// with a call to ProjectileParameters__Unserialize (0x8E8910) — the SAME blob op35/62 carries. So the
// nested projectile is our shared ProjectileParameters (docs/projectile-wire-spec.md), no longer opaque.
// The two lead-in sub-reads are also confirmed: sub_A2FAB0 = a length-prefixed Target list;
// sub_8DADD0 = an ActionBarData { Id, Slot } pair. Field MEANINGS past the projectile are still unknown
// (typed/ordered only) — named, never invented.
public class AbilityPacketLaunchAndLand : BaseAbilityPacket, ISerializablePacket
{
    public new const short OpCode = 4;

    public ulong Guid;

    // sub_A2FAB0: a length-prefixed list of polymorphic Targets. Empty in every safe case (a non-zero
    // count makes the client parse items through the type factory — a bad id null-derefs).
    public List<Target> Targets = [];

    public int Unknown2;
    public int Unknown3;
    public int Unknown4;
    public int Unknown5;
    public int Unknown6;

    public bool Unknown7;
    public bool Unknown8;

    public int Unknown9;
    public int Unknown10;
    public int Unknown11;

    public Vector4 TargetLocation;

    public int Unknown13;
    public int Unknown14;
    public int Unknown15;

    // sub_8DADD0: ActionBarData { int Id, int Slot }.
    public int ActionBarId;
    public int ActionBarSlot;

    public int Unknown17;

    public ulong TargetGuid;

    public bool Unknown19;

    public ProjectileParameters Projectile = new();

    public AbilityPacketLaunchAndLand() : base(OpCode)
    {
    }

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();

        base.Write(writer);

        writer.Write(Guid);

        writer.Write(Targets.Count);
        foreach (var target in Targets)
            target.Serialize(writer);

        writer.Write(Unknown2);
        writer.Write(Unknown3);
        writer.Write(Unknown4);
        writer.Write(Unknown5);
        writer.Write(Unknown6);

        writer.Write(Unknown7);
        writer.Write(Unknown8);

        writer.Write(Unknown9);
        writer.Write(Unknown10);
        writer.Write(Unknown11);

        writer.Write(TargetLocation);

        writer.Write(Unknown13);
        writer.Write(Unknown14);
        writer.Write(Unknown15);

        writer.Write(ActionBarId);
        writer.Write(ActionBarSlot);

        writer.Write(Unknown17);

        writer.Write(TargetGuid);

        writer.Write(Unknown19);

        Projectile.Serialize(writer);

        return writer.Buffer;
    }
}
