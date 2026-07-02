using Sanctuary.Core.IO;

namespace Sanctuary.Packet;

// INSTANCE WIP (Frostfang Fury): BaseEncounterPacket (op41) sub-opcode 107 = "EncounterZoneIsReadyPacket" (S2C).
//
// THE HANDSHAKE (RE'd from IDA 2026-06-24): the offer popup (sub114) shows with the loading SPINNER
// ("HandlerMiniGameStart:setNotReady"). The client dispatch (ClientCommandProcessor::sub_AA36C0) case 107 then
// calls ClientMiniGameManager::sub_9B0CC0, which does "HandlerMiniGameStart:setReady" + fires the
// "MiniGame:SetMiniGameReady" event â€” i.e. it flips the spinner into the green GO! button and renders the detail.
// sub_9B0CC0 takes NO packet fields (it ignores the contents) â€” so to trigger it we just need to send a sub107
// packet the client can deserialize WITHOUT hitting end-of-buffer (else it's silently rejected).
//
// EXACT FORMAT (IDA 2026-06-24, decompiled the deserializer sub_A9F990): the packet is JUST the 12-byte
// BaseEncounter header â€” NOTHING ELSE. sub_A9F990 reads only the header (sub_8D6690 = [short op][short sub]
// [int Unknown][int Unknown2]) then returns `a4 || byEnd-byData <= 0`; the dispatch passes a4=0, so it requires
// ZERO leftover bytes. (Our earlier defensive +8 bytes left 8 unconsumed â†’ returned false â†’ sub_9B0CC0 never
// ran â†’ spinner never cleared. Lesson: this validator fails on UNDER-read AND on any leftover.)
// On success, case 107 calls ClientMiniGameManager::sub_9B0CC0 ("HandlerMiniGameStart:setReady") + stores
// m_nUnknown2 and inserts m_nUnknown (the encounter id, header int) into the ready set. Keep Unknown = the
// offer's encounter id (both 0 here) so it matches.
public class EncounterZoneIsReadyPacket : BaseEncounterPacket, ISerializablePacket
{
    public new const short OpCode = 107;

    public EncounterZoneIsReadyPacket() : base(OpCode)
    {
    }

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();

        Write(writer);       // [op41][sub107][int Unknown][int Unknown2] â€” the ENTIRE packet (12 bytes)

        return writer.Buffer;
    }
}
