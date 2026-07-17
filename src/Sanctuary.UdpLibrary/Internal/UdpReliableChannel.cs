using System;
using System.Buffers.Binary;
using System.Diagnostics;

using Collections.Pooled;

using Sanctuary.Core.IO;
using Sanctuary.UdpLibrary.Configuration;
using Sanctuary.UdpLibrary.Enumerations;
using Sanctuary.UdpLibrary.Packets;

namespace Sanctuary.UdpLibrary.Internal;

internal class UdpReliableChannel
{
    private const int ReadyQueueSize = 1000;

    private readonly UdpReliableConfig Config;
    private readonly UdpConnection UdpConnection;

    private UdpClockStamp LastTimeStampAcknowledged;
    private UdpClockStamp TrickleLastSend;
    private UdpClockStamp NextNeedTime;
    private UdpClockStamp WindowResetTime;
    private readonly int ChannelNumber;
    private long ReliableOutgoingId;
    private long ReliableOutgoingPendingId;
    private int ReliableOutgoingBytes;
    private int LogicalBytesQueued;
    private byte[]? BigDataPtr;
    private int BigDataLen;
    private int BigDataTargetLen;
    private UdpClockStamp AveragePingTime;
    private int MaxDataBytes;
    private int FragmentNextPos;
    private PhysicalPacket[] PhysicalPackets;
    private PooledList<LogicalPacket> LogicalPacketList = new();

    private int CongestionWindowStart;
    private int CongestionWindowSize;
    private int CongestionSlowStartThreshhold;
    private int CongestionWindowMinimum;
    private bool MaxxedOutCurrentWindow;

    private long ReliableIncomingId;
    private IncomingQueueEntry[] ReliableIncoming;

    private LogicalPacket? CoalescePacket;
    private int CoalesceOffset;
    private int CoalesceCount;
    private int MaxCoalesceAttemptBytes;

    private byte[] BufferedAckPtr;

    private int StatDuplicatePacketsReceived;
    private int StatResentPacketsAccelerated;
    private int StatResentPacketsTimedOut;

    private struct PhysicalPacket
    {
        public UdpClockStamp FirstTimeStamp;
        public UdpClockStamp LastTimeStamp;
        public LogicalPacket? Parent;
        public int? DataPtr;
        public int DataLen;
    }

    private struct IncomingQueueEntry
    {
        public LogicalPacket? Packet;
        public ReliablePacketMode Mode;

        public IncomingQueueEntry()
        {
            Mode = ReliablePacketMode.Reliable;
        }
    }

    private PhysicalPacket[] ReadyQueue;

    public UdpReliableChannel(int channelNumber, UdpConnection con, UdpReliableConfig config)
    {
        UdpConnection = con;
        ChannelNumber = channelNumber;
        Config = config;
        Config.MaxOutstandingPackets = Math.Min(Config.MaxOutstandingPackets, Constants.HardMaxOutstandingPackets);

        AveragePingTime = 800;
        TrickleLastSend = 0;

        var fragmentSize = Config.FragmentSize;

        if (fragmentSize == 0 || fragmentSize > UdpConnection.ConnectionConfig.MaxRawPacketSize)
            fragmentSize = UdpConnection.ConnectionConfig.MaxRawPacketSize;

        MaxDataBytes = fragmentSize - Constants.UdpPacketReliableSize - UdpConnection.ConnectionConfig.CrcBytes - UdpConnection.EncryptExpansionBytes;

        Debug.Assert(MaxDataBytes > 0);

        if (Config.TrickleSize != 0)
            MaxDataBytes = Math.Min(MaxDataBytes, Config.TrickleSize);

        MaxCoalesceAttemptBytes = -1;

        if (Config.Coalesce)
            MaxCoalesceAttemptBytes = MaxDataBytes - 5;

        ReliableIncomingId = 0;
        ReliableOutgoingId = 0;
        ReliableOutgoingPendingId = 0;
        ReliableOutgoingBytes = 0;
        LogicalBytesQueued = 0;

        CoalescePacket = null;
        CoalesceOffset = 0;
        CoalesceCount = 0;

        BufferedAckPtr = Array.Empty<byte>();

        StatDuplicatePacketsReceived = 0;
        StatResentPacketsAccelerated = 0;
        StatResentPacketsTimedOut = 0;

        WindowResetTime = 0;

        CongestionWindowMinimum = Math.Max(MaxDataBytes, Config.CongestionWindowMinimum);
        CongestionWindowStart = Math.Min(4 * MaxDataBytes, Math.Max(2 * MaxDataBytes, 4380));
        CongestionWindowStart = Math.Max(CongestionWindowStart, Config.CongestionWindowMaximum);
        CongestionSlowStartThreshhold = Math.Min(Config.MaxOutstandingPackets * MaxDataBytes, Config.MaxOutstandingBytes);
        CongestionWindowSize = CongestionWindowStart;

        BigDataLen = 0;
        BigDataTargetLen = 0;
        BigDataPtr = null;
        FragmentNextPos = 0;
        LastTimeStampAcknowledged = 0;
        MaxxedOutCurrentWindow = false;
        NextNeedTime = 0;

        PhysicalPackets = new PhysicalPacket[Config.MaxOutstandingPackets];
        ReliableIncoming = new IncomingQueueEntry[Config.MaxInstandingPackets];

        ReadyQueue = new PhysicalPacket[ReadyQueueSize];
    }

    public int TotalPendingBytes()
    {
        return LogicalBytesQueued + ReliableOutgoingBytes;
    }

    public void ReliablePacket(Span<byte> data)
    {
        if (data.Length <= Constants.UdpPacketReliableSize)
        {
            UdpConnection.CallbackCorruptPacket(data, UdpCorruptionReason.ReliablePacketTooShort);
            return;
        }

        var packetType = data[1];

        var reliableStamp = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(2));

        var reliableId = GetReliableIncomingId(reliableStamp);

        if (reliableId >= ReliableIncomingId + Config.MaxInstandingPackets)
            return;

        if (reliableId >= ReliableIncomingId)
        {
            var mode = (ReliablePacketMode)((packetType - (byte)UdpPacketType.Reliable1) / Constants.ReliableChannelCount);

            if (ReliableIncomingId == reliableId)
            {
                ProcessPacket(mode, data.Slice(Constants.UdpPacketReliableSize));

                ReliableIncomingId++;

                ref var packet = ref ReliableIncoming[ReliableIncomingId % Config.MaxInstandingPackets].Packet;

                while (packet is not null)
                {
                    var spot = (int)(ReliableIncomingId % Config.MaxInstandingPackets);

                    if (ReliableIncoming[spot].Mode != ReliablePacketMode.Delivered)
                        ProcessPacket(ReliableIncoming[spot].Mode, packet.GetDataPtr().Slice(0, packet.GetDataLen()));

                    packet = null;
                    ReliableIncomingId++;

                    packet = ref ReliableIncoming[ReliableIncomingId % Config.MaxInstandingPackets].Packet;
                }
            }
            else
            {

                var spot = (int)(reliableId % Config.MaxInstandingPackets);

                ref var packet = ref ReliableIncoming[spot].Packet;

                if (packet is null)
                {
                    ReliableIncoming[spot].Mode = mode;

                    packet = UdpConnection.UdpManager.CreatePacket(data.Slice(Constants.UdpPacketReliableSize), data.Length - Constants.UdpPacketReliableSize);

                    ReliableIncoming[spot].Packet = packet;

                    if (mode == ReliablePacketMode.Reliable && Config.OutOfOrder)
                    {
                        ProcessPacket(ReliablePacketMode.Reliable, packet.GetDataPtr().Slice(0, packet.GetDataLen()));
                        ReliableIncoming[spot].Mode = ReliablePacketMode.Delivered;
                    }
                }
            }
        }
        else
        {
            StatDuplicatePacketsReceived++;
            UdpConnection.ConnectionStats.DuplicatePacketsReceived++;
            UdpConnection.UdpManager.IncrementDuplicatePacketsReceived();
        }

        var ackAll = false;

        var buf = new byte[4];

        var bufPtr = buf.AsSpan();

        bufPtr[0] = 0;

        if (ReliableIncomingId > reliableId)
        {
            bufPtr[1] = (byte)((byte)UdpPacketType.AckAll1 + ChannelNumber);
            BinaryPrimitives.WriteUInt16BigEndian(bufPtr.Slice(2), (ushort)(ReliableIncomingId - 1));
            ackAll = true;
        }
        else
        {
            bufPtr[1] = (byte)((byte)UdpPacketType.Ack1 + ChannelNumber);
            BinaryPrimitives.WriteUInt16BigEndian(bufPtr.Slice(2), (ushort)reliableId);
        }

        var bufferedAckPtr = BufferedAckPtr.AsSpan();

        if (!bufferedAckPtr.IsEmpty && Config.AckDeduping && ackAll)
        {
            bufPtr.CopyTo(BufferedAckPtr);
        }
        else
        {
            var ptr = UdpConnection.BufferedSend(bufPtr, bufPtr.Length, null, 0, true);

            if (bufferedAckPtr.IsEmpty)
            {
                bufferedAckPtr = ptr;
            }
        }
    }

    public void Send(Span<byte> data, int dataLen, Span<byte> data2, int dataLen2)
    {
        if (LogicalPacketList.Count == 0 && CoalescePacket is null)
        {
            NextNeedTime = 0;
            UdpConnection.ScheduleTimeNow();
        }

        if (dataLen + dataLen2 <= MaxCoalesceAttemptBytes)
        {
            SendCoalesce(data, dataLen, data2, dataLen2);
        }
        else
        {
            FlushCoalesce();

            var packet = UdpConnection.UdpManager.CreatePacket(data, dataLen, data2, dataLen2);

            QueueLogicalPacket(packet);
        }
    }

    public void AckPacket(Span<byte> data)
    {
        if (data.Length < 4)
        {
            UdpConnection.CallbackCorruptPacket(data, UdpCorruptionReason.AckBad);
            return;
        }

        var reader = new PacketReader(data.Slice(2));

        if (!reader.TryReadUInt16(out var reliableStamp))
        {
            UdpConnection.CallbackCorruptPacket(data, UdpCorruptionReason.AckBad);
            return;
        }

        Ack(GetReliableOutgoingId(reliableStamp));
    }

    private long GetReliableOutgoingId(int reliableStamp)
    {

        var reliableId = (long)reliableStamp | ReliableOutgoingId & ~0xffffL;

        if (reliableId > ReliableOutgoingId)
            reliableId -= 0x10000;

        return reliableId;
    }

    public void AckAllPacket(Span<byte> data)
    {
        if (data.Length < 4)
        {
            UdpConnection.CallbackCorruptPacket(data, UdpCorruptionReason.AckBad);
            return;
        }

        var reader = new PacketReader(data.Slice(2));

        if (!reader.TryReadUInt16(out ushort reliableStamp))
        {
            UdpConnection.CallbackCorruptPacket(data, UdpCorruptionReason.AckBad);
            return;
        }

        var reliableId = GetReliableOutgoingId(reliableStamp);

        if (ReliableOutgoingPendingId > reliableId)
        {
            AveragePingTime += 400;
            AveragePingTime = Math.Min(Config.ResendDelayCap, AveragePingTime);
        }

        for (var i = ReliableOutgoingPendingId; i <= reliableId; i++)
            Ack(i);
    }

    public void ClearBufferedAck()
    {
        BufferedAckPtr = Array.Empty<byte>();
    }

    public UdpClockStamp GiveTime()
    {
        var hotClock = UdpConnection.UdpManager.CachedClock;

        if (hotClock < NextNeedTime)
            return UdpMisc.ClockDiff(hotClock, NextNeedTime);

        if (Config.TrickleRate > 0)
        {
            var nextAllowedSendTime = Config.TrickleRate - UdpMisc.ClockDiff(TrickleLastSend, hotClock);

            if (nextAllowedSendTime > 0)
                return nextAllowedSendTime;
        }

        var optimalResendDelay = AveragePingTime * Config.ResendDelayPercent / 100 + Config.ResendDelayAdjust;

        optimalResendDelay = Math.Min(Config.ResendDelayCap, optimalResendDelay);

        MaxxedOutCurrentWindow = false;

        var outstandingNextSendTime = 10 * 60000L;

        if (ReliableOutgoingPendingId < ReliableOutgoingId || LogicalPacketList.Count != 0 || CoalescePacket is not null)
        {
            var oldestResendTime = Math.Max(hotClock - optimalResendDelay, LastTimeStampAcknowledged);

            bool readyQueueOverflow;

            Span<byte> buf = stackalloc byte[8];

            do
            {
                readyQueueOverflow = false;

                var useMaxOutstandingBytes = Math.Min(Config.MaxOutstandingBytes, CongestionWindowSize);

                var outstandingBytes = 0;

                var readyEnd = ReadyQueue.Length;
                var readyPtr = 0;

                var windowSpaceLeft = useMaxOutstandingBytes;

                for (var i = ReliableOutgoingPendingId; i <= ReliableOutgoingId; i++)
                {
                    if (i == ReliableOutgoingId)
                    {
                        if (!PullDown(windowSpaceLeft))
                            break;
                    }

                    ref var entry = ref PhysicalPackets[i % Config.MaxOutstandingPackets];

                    if (entry.DataPtr.HasValue)
                    {

                        windowSpaceLeft -= entry.DataLen;

                        if (entry.LastTimeStamp < oldestResendTime)
                        {
                            if (readyPtr < readyEnd)
                            {
                                ReadyQueue[readyPtr++] = entry;
                            }
                            else
                            {
                                readyQueueOverflow = true;
                            }
                        }
                        else
                        {
                            outstandingBytes += entry.DataLen;
                            outstandingNextSendTime = Math.Min(outstandingNextSendTime, optimalResendDelay - UdpMisc.ClockDiff(entry.LastTimeStamp, hotClock));
                        }

                        if (entry.FirstTimeStamp == 0 && windowSpaceLeft <= 0)
                            break;
                    }
                }

                var toleranceLossCount = 0;

                var allowWindowReset = UdpMisc.ClockDiff(WindowResetTime, hotClock) > AveragePingTime;
                var trickleSent = 0;

                var readyWalk = 0;
                while (readyWalk < readyPtr && outstandingBytes < useMaxOutstandingBytes)
                {
                    ref var entry = ref ReadyQueue[readyWalk++];

                    ArgumentNullException.ThrowIfNull(entry.Parent);
                    ArgumentNullException.ThrowIfNull(entry.DataPtr);

                    var parentBase = entry.Parent.GetDataPtr();

                    var fragment = false;

                    if (entry.DataPtr.Value != 0 || entry.DataLen != entry.Parent.GetDataLen())
                        fragment = true;

                    var reliableId = ReliableOutgoingPendingId + (readyWalk - 1);

                    buf[0] = 0;

                    buf[1] = (byte)((fragment ? (byte)UdpPacketType.Fragment1 : (byte)UdpPacketType.Reliable1) + ChannelNumber);

                    BinaryPrimitives.WriteUInt16BigEndian(buf.Slice(2), (ushort)reliableId);

                    if (fragment && entry.DataPtr.Value == 0)
                    {
                        BinaryPrimitives.WriteInt32BigEndian(buf.Slice(4), entry.Parent.GetDataLen());
                        UdpConnection.BufferedSend(buf, 8, parentBase.Slice(entry.DataPtr.Value), entry.DataLen, false);
                    }
                    else
                    {
                        UdpConnection.BufferedSend(buf, 4, parentBase.Slice(entry.DataPtr.Value), entry.DataLen, false);
                    }

                    if (entry.FirstTimeStamp == 0)
                    {
                        entry.FirstTimeStamp = hotClock;
                    }
                    else
                    {
                        if (UdpConnection.UdpManager.Params.OldestUnacknowledgedTimeout > 0)
                        {
                            var age = UdpMisc.ClockDiff(entry.FirstTimeStamp, hotClock);
                            if (age > UdpConnection.UdpManager.Params.OldestUnacknowledgedTimeout)
                            {
                                UdpConnection.InternalDisconnect(0, DisconnectReason.UnacknowledgedTimeout);
                                return 0;
                            }
                        }

                        if (entry.LastTimeStamp < LastTimeStampAcknowledged)
                        {
                            if (allowWindowReset && toleranceLossCount > Config.ToleranceLossCount)
                            {
                                allowWindowReset = false;
                                WindowResetTime = hotClock;
                                CongestionWindowSize = CongestionWindowSize * 3 / 4;
                                CongestionWindowSize = Math.Max(CongestionWindowMinimum, CongestionWindowSize);
                                CongestionSlowStartThreshhold = CongestionWindowSize;
                                useMaxOutstandingBytes = Math.Min(Config.MaxOutstandingBytes, CongestionWindowSize);
                            }

                            toleranceLossCount++;

                            StatResentPacketsAccelerated++;
                            UdpConnection.ConnectionStats.ResentPacketsAccelerated++;
                            UdpConnection.UdpManager.IncrementResentPacketsAccelerated();
                        }
                        else
                        {
                            if (allowWindowReset)
                            {
                                allowWindowReset = false;
                                WindowResetTime = hotClock;

                                CongestionSlowStartThreshhold = Math.Max(MaxDataBytes * 2, CongestionWindowSize / 2);
                                CongestionWindowSize = CongestionWindowStart;
                                useMaxOutstandingBytes = Math.Min(Config.MaxOutstandingBytes, CongestionWindowSize);

                                AveragePingTime += 100;

                                AveragePingTime = Math.Min(Config.ResendDelayCap, AveragePingTime);
                            }

                            StatResentPacketsTimedOut++;
                            UdpConnection.ConnectionStats.ResentPacketsTimedOut++;
                            UdpConnection.UdpManager.IncrementResentPacketsTimedOut();
                        }
                    }

                    entry.LastTimeStamp = hotClock;

                    outstandingNextSendTime = Math.Min(outstandingNextSendTime, optimalResendDelay);

                    outstandingBytes += entry.DataLen;
                    TrickleLastSend = hotClock;
                    trickleSent += entry.DataLen;

                    if (Config.TrickleSize != 0 && trickleSent >= Config.TrickleSize)
                        break;
                }

                if (outstandingBytes >= useMaxOutstandingBytes)
                {
                    MaxxedOutCurrentWindow = true;
                }

            } while (readyQueueOverflow && !MaxxedOutCurrentWindow);
        }
        else
        {
            CongestionWindowSize = CongestionWindowStart;
        }

        {
            var nextAllowedSendTime = Config.TrickleRate - UdpMisc.ClockDiff(TrickleLastSend, hotClock);

            nextAllowedSendTime = Math.Max(0, Math.Max(nextAllowedSendTime, outstandingNextSendTime));

            NextNeedTime = hotClock + nextAllowedSendTime;

            return nextAllowedSendTime;
        }
    }

    private long GetReliableIncomingId(int reliableStamp)
    {

        var reliableId = (long)reliableStamp | ReliableIncomingId & ~0xffffL;

        if (reliableId < ReliableIncomingId - Constants.HardMaxOutstandingPackets)
            reliableId += 0x10000;

        if (reliableId > ReliableIncomingId + Constants.HardMaxOutstandingPackets)
            reliableId -= 0x10000;

        return reliableId;
    }

    private void Ack(long reliableId)
    {
        if (reliableId >= ReliableOutgoingPendingId && reliableId < ReliableOutgoingId)
        {
            var pos = (int)(reliableId % Config.MaxOutstandingPackets);
            ref var entry = ref PhysicalPackets[pos];

            if (entry.DataPtr.HasValue)
            {
                NextNeedTime = 0;

                if (MaxxedOutCurrentWindow)
                {
                    if (CongestionWindowSize < CongestionSlowStartThreshhold)
                    {
                        CongestionWindowSize += MaxDataBytes;
                    }
                    else
                    {
                        var increase = MaxDataBytes * MaxDataBytes / CongestionWindowSize;

                        CongestionWindowSize += Math.Max(1, increase);
                    }
                }

                if (entry.LastTimeStamp == entry.FirstTimeStamp)
                {
                    var thisPingTime = UdpConnection.UdpManager.CachedClockElapsed(entry.FirstTimeStamp);
                    AveragePingTime = (AveragePingTime * 3 + thisPingTime) / 4;
                }

                LastTimeStampAcknowledged = entry.FirstTimeStamp;

                ReliableOutgoingBytes -= entry.DataLen;
                entry.DataLen = 0;
                entry.DataPtr = null;
                entry.Parent = null;

                while (ReliableOutgoingPendingId < ReliableOutgoingId)
                {
                    if (PhysicalPackets[ReliableOutgoingPendingId % Config.MaxOutstandingPackets].DataPtr.HasValue)
                        break;

                    ReliableOutgoingPendingId++;
                }
            }
            else
            {
            }
        }

    }

    private void ProcessPacket(ReliablePacketMode mode, Span<byte> data)
    {
        var reader = new PacketReader(data);

        if (mode == ReliablePacketMode.Reliable)
        {
            if (BigDataPtr is not null)
            {
                UdpConnection.CallbackCorruptPacket(data, UdpCorruptionReason.FragmentExpected);
                return;
            }

            UdpConnection.ProcessCookedPacket(data);
        }
        else if (mode == ReliablePacketMode.Fragment)
        {
            if (BigDataPtr is null)
            {
                if (data.Length < 4)
                {
                    UdpConnection.CallbackCorruptPacket(data, UdpCorruptionReason.FragmentBad);
                    return;
                }

                if (!reader.TryReadInt32(out BigDataTargetLen))
                    return;

                if (BigDataTargetLen <= 0)
                {
                    UdpConnection.CallbackCorruptPacket(data, UdpCorruptionReason.FragmentBad);
                    return;
                }

                if (BigDataTargetLen > UdpConnection.UdpManager.Params.IncomingLogicalPacketMax)
                {
                    UdpConnection.CallbackCorruptPacket(data, UdpCorruptionReason.FragmentOversized);
                    return;
                }

                BigDataPtr = new byte[BigDataTargetLen];
                BigDataLen = 0;
            }

            var safetyMax = Math.Min(BigDataTargetLen - BigDataLen, reader.RemainingLength);

            Debug.Assert(safetyMax == reader.RemainingLength);

            reader.RemainingSpan.CopyTo(BigDataPtr.AsSpan(BigDataLen));
            BigDataLen += safetyMax;

            if (BigDataTargetLen == BigDataLen)
            {
                UdpConnection.ProcessCookedPacket(BigDataPtr.AsSpan(0, BigDataLen));

                BigDataLen = 0;
                BigDataTargetLen = 0;
                BigDataPtr = null;
            }
        }
    }

    private bool PullDown(int windowSpaceLeft)
    {

        var pulledDown = false;
        var physicalCount = ReliableOutgoingId - ReliableOutgoingPendingId;

        while (windowSpaceLeft > 0 && physicalCount < Config.MaxOutstandingPackets)
        {
            if (LogicalPacketList.Count == 0)
            {
                FlushCoalesce();

                if (LogicalPacketList.Count == 0)
                    break;
            }

            var nextSpot = ReliableOutgoingId % Config.MaxOutstandingPackets;

            ref var entry = ref PhysicalPackets[nextSpot];
            entry.Parent = LogicalPacketList[0];
            entry.FirstTimeStamp = 0;
            entry.LastTimeStamp = 0;

            var dataLen = entry.Parent.GetDataLen();

            var bytesLeft = dataLen - FragmentNextPos;
            var bytesToSend = Math.Min(bytesLeft, MaxDataBytes);

            entry.DataPtr = FragmentNextPos;

            if (bytesToSend != dataLen)
            {
                if (FragmentNextPos == 0)
                    bytesToSend -= 4;
            }

            entry.DataLen = bytesToSend;
            ReliableOutgoingBytes += bytesToSend;

            if (bytesToSend == bytesLeft)
            {
                FragmentNextPos = 0;
                LogicalPacketList.Remove(entry.Parent);
            }
            else
            {
                FragmentNextPos += bytesToSend;
            }

            LogicalBytesQueued -= bytesToSend;

            ReliableOutgoingId++;

            physicalCount++;

            windowSpaceLeft -= bytesToSend;

            pulledDown = true;
        }

        return pulledDown;
    }

    private void FlushCoalesce()
    {
        if (CoalescePacket is null)
            return;

        if (CoalesceCount == 1)
        {
            var dataPtr = CoalescePacket.GetDataPtr();

            var skipLen = UdpMisc.GetVariableValue(dataPtr.Slice(2), out var firstLen);
            dataPtr.Slice(2 + skipLen, firstLen).CopyTo(dataPtr);

            CoalesceOffset = firstLen;
        }

        CoalescePacket.SetDataLen(CoalesceOffset);
        QueueLogicalPacket(CoalescePacket);
        CoalescePacket = null;
    }

    private void SendCoalesce(Span<byte> data, int dataLen, Span<byte> data2 = default, int dataLen2 = 0)
    {
        var totalLen = dataLen + dataLen2;

        if (CoalescePacket is null)
        {
            CoalescePacket = UdpConnection.UdpManager.CreatePacket(null, MaxDataBytes);

            CoalesceOffset = 0;

            var dataPtr = CoalescePacket.GetDataPtr();

            dataPtr[CoalesceOffset++] = 0;
            dataPtr[CoalesceOffset++] = (byte)UdpPacketType.Group;

            CoalesceCount = 0;
        }
        else
        {
            var spaceLeft = MaxDataBytes - CoalesceOffset;

            if (totalLen + 3 > spaceLeft)
            {
                FlushCoalesce();
                SendCoalesce(data, dataLen, data2, dataLen2);
                return;
            }
        }

        CoalesceCount++;

        {
            var dataPtr = CoalescePacket.GetDataPtr();

            CoalesceOffset += UdpMisc.PutVariableValue(dataPtr.Slice(CoalesceOffset), totalLen);

            if (!data.IsEmpty)
                data.Slice(0, dataLen).CopyTo(dataPtr.Slice(CoalesceOffset));

            CoalesceOffset += dataLen;

            if (!data2.IsEmpty)
                data2.Slice(0, dataLen2).CopyTo(dataPtr.Slice(CoalesceOffset));

            CoalesceOffset += dataLen2;
        }
    }

    private void QueueLogicalPacket(LogicalPacket packet)
    {
        LogicalBytesQueued += packet.GetDataLen();
        LogicalPacketList.Add(packet);
    }

    public UdpClockStamp GetAveragePing()
    {
        return AveragePingTime;
    }
}
