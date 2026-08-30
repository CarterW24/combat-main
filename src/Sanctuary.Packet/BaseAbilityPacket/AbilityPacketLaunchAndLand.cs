using System.Collections.Generic;
using System.Numerics;

using Sanctuary.Core.IO;
using Sanctuary.Packet.Common;
using Sanctuary.Packet.Common.Targets;

namespace Sanctuary.Packet;

public class AbilityPacketLaunchAndLand : BaseAbilityPacket, ISerializablePacket
{
    public new const short OpCode = 4;

    public ulong Guid;

    public List<Target> Targets = [];

    public int HasProjectile;
    public int Unknown3;
    public int CasterAnimationId;
    public int CasterEffectId;
    public int RecastMs;

    public bool IsDefaultAttack;
    public bool Unknown8;

    public int TargetAnimationId;
    public int TargetEffectId;
    public int NoTargetMode;

    public Vector4 TargetLocation;

    public float CasterEffectDuration;
    public float TargetEffectDuration;
    public int Unknown15;

    public int ActionBarId;
    public int ActionBarSlot;

    public int ScheduleMeleeContact;

    public ulong TargetGuid;

    public bool SuppressCastStartEvent;

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

        writer.Write(HasProjectile);
        writer.Write(Unknown3);
        writer.Write(CasterAnimationId);
        writer.Write(CasterEffectId);
        writer.Write(RecastMs);

        writer.Write(IsDefaultAttack);
        writer.Write(Unknown8);

        writer.Write(TargetAnimationId);
        writer.Write(TargetEffectId);
        writer.Write(NoTargetMode);

        writer.Write(TargetLocation);

        writer.Write(CasterEffectDuration);
        writer.Write(TargetEffectDuration);
        writer.Write(Unknown15);

        writer.Write(ActionBarId);
        writer.Write(ActionBarSlot);

        writer.Write(ScheduleMeleeContact);

        writer.Write(TargetGuid);

        writer.Write(SuppressCastStartEvent);

        Projectile.Serialize(writer);

        return writer.Buffer;
    }
}
