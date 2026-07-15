using Sanctuary.Core.IO;

namespace Sanctuary.Packet;

// BaseAbilityPacket (op 36) sub-opcode 13 = "AbilityDefinition" — the client's answer to its own op36/12
// RequestAbilityDefinition. The client inserts the decoded record into its HashListMap<int, AbilityDefinition>
// keyed by AbilityId; the AbilitiesScreen then resolves an ability's Name/Description/Icon from that map
// (bridge GetAbilityDescription -> map lookup). If the id isn't in the map the screen renders "undefined".
//
// ★ WIRE FORMAT REVERSED 2026-07-15 from the client field reader (FUN_00a32930, reached via the op36 dispatcher
// FUN_00a35cc0 -> deserialize FUN_00a34380). It is a LARGE fixed record (NOT the old 6-int stub, which the
// client mis-parsed so nothing was ever inserted -> the "undefined" columns). Field meanings recovered from the
// struct's readers: +0x10 Name text id, +0x14 Description text id, +0x18 Icon, +0x1c CastSeconds(float),
// +0x38 ManaCost, +0x58 ManaCostPerSecond, +0x60 AuraDuration, +0x68 MaxAoeTargets. The rest we don't need are
// sent as 0; the record ends with an empty variable list (count 0) and a trailing bool. Struct offsets are in
// the comments so the layout can be re-checked against the decompiler.
public class AbilityPacketAbilityDefinition : BaseAbilityPacket, ISerializablePacket
{
    public new const short OpCode = 13;

    public int AbilityId;          // +0x08 — the map key (the requested ability def id)
    public int NameId;             // +0x10 — Global.Text id shown as the ability name
    public int DescriptionId;      // +0x14 — Global.Text id shown as the description
    public int IconId;             // +0x18 — ability icon image id
    public float CastSeconds;      // +0x1c
    public int ManaCost;           // +0x38
    public int ManaCostPerSecond;  // +0x58
    public int AuraDuration;       // +0x60
    public int MaxAoeTargets;      // +0x68

    public AbilityPacketAbilityDefinition() : base(OpCode)
    {
    }

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();

        Write(writer); // op 36 + sub 13

        writer.Write(AbilityId);         // +0x08
        writer.Write(false);             // +0x0c
        writer.Write(false);             // +0x0d
        writer.Write(NameId);            // +0x10
        writer.Write(DescriptionId);     // +0x14
        writer.Write(IconId);            // +0x18
        writer.Write(CastSeconds);       // +0x1c (float)
        writer.Write(0);                 // +0x20
        writer.Write(0);                 // +0x24
        writer.Write(0);                 // +0x28
        writer.Write(0);                 // +0x2c
        writer.Write(0);                 // +0x30
        writer.Write(0);                 // +0x34
        writer.Write(ManaCost);          // +0x38
        writer.Write(0);                 // +0x3c
        writer.Write(0);                 // +0x40
        writer.Write(0f);                // +0x44 (float)
        writer.Write(0f);                // +0x48 (float)
        writer.Write(0);                 // +0x4c
        writer.Write(0);                 // +0x50
        writer.Write(ManaCostPerSecond); // +0x58
        writer.Write(false);             // +0x5c
        writer.Write(AuraDuration);      // +0x60
        writer.Write(0);                 // +0x64
        writer.Write(MaxAoeTargets);     // +0x68
        writer.Write(0f);                // +0x6c (float)
        writer.Write(0);                 // +0x70
        writer.Write(0);                 // +0x74
        writer.Write(0f);                // +0x78 (float)
        writer.Write(0f);                // +0x7c (float)
        writer.Write(0);                 // +0x80
        writer.Write(0);                 // +0x84
        writer.Write(0);                 // +0x88
        writer.Write(0f);                // +0x8c (float)
        writer.Write(0f);                // +0x90 (float)
        writer.Write(false);             // +0x94
        writer.Write(0);                 // +0x98
        writer.Write(0);                 // +0x9c
        writer.Write(false);             // +0xa0
        writer.Write(false);             // +0xa1
        writer.Write(false);             // +0xa2
        writer.Write(0);                 // +0xa4
        writer.Write(0f);                // +0xa8 (float)
        writer.Write(0);                 // +0xb0 — variable list: count 0 (no entries)
        writer.Write(false);             // +0xad — trailing bool (read after the list)

        return writer.Buffer;
    }
}
