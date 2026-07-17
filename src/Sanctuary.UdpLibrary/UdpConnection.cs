using System;
using System.Buffers.Binary;
using System.Diagnostics;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

using Sanctuary.Core.IO;
using Sanctuary.UdpLibrary.Abstractions;
using Sanctuary.UdpLibrary.Enumerations;
using Sanctuary.UdpLibrary.Internal;
using Sanctuary.UdpLibrary.Packets;
using Sanctuary.UdpLibrary.Statistics;

namespace Sanctuary.UdpLibrary;

public class UdpConnection : PriorityQueueMember
{
    private readonly Lock _guard = new();
    private readonly Lock _handlerGuard = new();

    public IPEndPoint EndPoint { get; internal set; }
    public SocketAddress SocketAddress { get; internal set; }

    public Status Status { get; private set; }

    public DisconnectReason DisconnectReason { get; private set; }
    public DisconnectReason OtherSideDisconnectReason { get; private set; }

    internal IUdpManager UdpManager;

    internal int ConnectCode;

    internal UdpConnectionStatistics ConnectionStats;

    private UdpClockStamp ConnectionCreateTime;
    private UdpClockStamp ConnectAttemptTimeout;
    public UdpClockStamp NoDataTimeout;

    private bool FlaggedPortUnreachable;
    private bool SilentDisconnect;

    private UdpReliableChannel[] Channel;

    internal class Configuration
    {
        public int EncryptCode;
        public byte CrcBytes;
        public EncryptMethod[] EncryptMethod = new EncryptMethod[Constants.EncryptPasses];
        public int MaxRawPacketSize;
    }

    internal Configuration ConnectionConfig = new();

    private int OtherSideProtocolVersion;
    private string? OtherSideProtocolName;

    private UdpClockStamp LastClockSyncTime;
    private UdpClockStamp DataHoldTime;
    private UdpClockStamp LastSendTime;
    private UdpClockStamp LastReceiveTime;
    private UdpClockStamp LastPortAliveTime;

    private byte[] MultiBufferData;
    private int MultiBufferOffset;

    private int OrderedCountOutgoing;
    private int OrderedCountOutgoing2;
    private ushort OrderedStampLast;
    private ushort OrderedStampLast2;

    private byte[]? _encryptXorBuffer;
    internal int EncryptExpansionBytes;

    private uint SyncTimeDelta;
    private uint SyncStatTotal;
    private uint SyncStatCount;
    private uint SyncStatLow;
    private uint SyncStatHigh;
    private uint SyncStatLast;
    private uint SyncStatMasterRoundTime;
    private UdpClockStamp SyncStatMasterFixupTime;

    private bool GettingTime;

    private int KeepAliveDelay;

    private UdpClockStamp IcmpErrorRetryStartStamp;
    private UdpClockStamp PortRemapRequestStartStamp;

    private UdpClockStamp DisconnectFlushStamp;
    private UdpClockStamp DisconnectFlushTimeout;

    private delegate int CryptFunction(Span<byte> destData, Span<byte> sourceData);

    private readonly byte[][] _tempDecryptBuffer;
    private readonly byte[][] _tempEncryptBuffer;
    private CryptFunction[] DecryptFunction = new CryptFunction[Constants.EncryptPasses];
    private CryptFunction[] EncryptFunction = new CryptFunction[Constants.EncryptPasses];

    public UdpConnection(IUdpManager udpManager, SocketAddress socketAddress, UdpClockStamp timeout) : this(udpManager, socketAddress)
    {
        lock (_guard)
        {
            ConnectAttemptTimeout = timeout;

            Status = Status.Negotiating;

            ConnectCode = UdpManager.Random();

            GiveTime();
        }
    }

    public UdpConnection(IUdpManager udpManager, SocketAddress socketAddress, int connectCode) : this(udpManager, socketAddress)
    {
        lock (_guard)
        {
            Status = Status.Connected;

            ConnectionConfig.EncryptMethod = new EncryptMethod[Constants.EncryptPasses];

            for (var i = 0; i < Constants.EncryptPasses; i++)
                ConnectionConfig.EncryptMethod[i] = UdpManager.Params.EncryptMethod[i];

            ConnectionConfig.CrcBytes = UdpManager.Params.CrcBytes;
            ConnectionConfig.MaxRawPacketSize = UdpManager.Params.MaxRawPacketSize;
            ConnectionConfig.EncryptCode = UdpManager.Random();

            SetupEncryptModel();

            ConnectCode = connectCode;
        }
    }

    private UdpConnection(IUdpManager udpManager, SocketAddress socketAddress)
    {
        UdpManager = udpManager;

        SocketAddress = new SocketAddress(socketAddress.Family, socketAddress.Size);
        socketAddress.Buffer.CopyTo(SocketAddress.Buffer);

        var tempEndPoint = new IPEndPoint(IPAddress.Any, 0);
        EndPoint = (IPEndPoint)tempEndPoint.Create(socketAddress);

        FlaggedPortUnreachable = false;

        LastPortAliveTime = LastSendTime = 0;
        LastReceiveTime = UdpManager.CachedClock;
        LastClockSyncTime = 0;
        DataHoldTime = 0;
        GettingTime = false;
        OtherSideProtocolVersion = 0;
        OtherSideProtocolName = null;

        NoDataTimeout = UdpManager.Params.NoDataTimeout;
        KeepAliveDelay = UdpManager.Params.KeepAliveDelay;

        MultiBufferData = GC.AllocateArray<byte>(UdpManager.Params.MaxRawPacketSize, true);
        MultiBufferOffset = 0;

        IcmpErrorRetryStartStamp = 0;
        PortRemapRequestStartStamp = 0;

        _encryptXorBuffer = null;
        EncryptExpansionBytes = 0;
        OrderedCountOutgoing = 0;
        OrderedCountOutgoing2 = 0;
        OrderedStampLast = 0;
        OrderedStampLast2 = 0;
        DisconnectReason = DisconnectReason.None;
        OtherSideDisconnectReason = DisconnectReason.None;

        ConnectAttemptTimeout = 0;
        ConnectionCreateTime = UdpManager.CachedClock;
        SilentDisconnect = false;

        PingStatReset();
        SyncTimeDelta = 0;

        Channel = new UdpReliableChannel[Constants.ReliableChannelCount];

        _tempDecryptBuffer = new byte[Constants.EncryptPasses][];
        _tempEncryptBuffer = new byte[Constants.EncryptPasses][];

        for (var i = 0; i < Constants.EncryptPasses; i++)
        {
            _tempDecryptBuffer[i] = GC.AllocateArray<byte>(Constants.HardMaxRawPacketSize, true);
            _tempEncryptBuffer[i] = GC.AllocateArray<byte>(Constants.HardMaxRawPacketSize + sizeof(int), true);
        }
    }

    private void PortUnreachable()
    {
        if (!UdpManager.Params.ProcessIcmpErrors)
            return;

        if (!UdpManager.Params.ProcessIcmpErrorsDuringNegotiating)
        {
            if (Status == Status.Negotiating)
                return;
        }

        if (UdpManager.Params.IcmpErrorRetryPeriod != 0)
        {
            if (IcmpErrorRetryStartStamp == 0)
            {
                IcmpErrorRetryStartStamp = UdpManager.CachedClock;
                return;
            }

            if (UdpManager.CachedClockElapsed(IcmpErrorRetryStartStamp) < UdpManager.Params.IcmpErrorRetryPeriod)
            {
                return;
            }
        }

        InternalDisconnect(0, DisconnectReason.IcmpError);
    }

    internal void InternalDisconnect(int flushTimeout, DisconnectReason reason)
    {
        lock (_guard)
        {
            if (DisconnectReason == DisconnectReason.None)
                DisconnectReason = reason;

            if (Status == Status.Negotiating)
                flushTimeout = 0;

            if (UdpManager is null)
                return;

            if (flushTimeout > 0)
            {
                FlushMultiBuffer();

                DisconnectFlushStamp = UdpManager.CachedClock;
                DisconnectFlushTimeout = flushTimeout;

                ScheduleTimeNow();

                if (Status != Status.DisconnectPending)
                {
                    Status = Status.DisconnectPending;
                    UdpManager.KeepUntilDisconnected(this);
                }

                return;
            }

            if (!SilentDisconnect)
            {
                if (Status is Status.Connected or Status.DisconnectPending)
                {
                    SendTerminatePacket(ConnectCode, DisconnectReason);
                }
            }

            Status = Status.Disconnected;

            UdpManager.RemoveConnection(this);

            if (reason != DisconnectReason.ManagerDeleted)
                UdpManager.CallbackTerminated(this);
        }
    }

    private void SendTerminatePacket(int connectCode, DisconnectReason reason)
    {
        Span<byte> buf = stackalloc byte[8 + 4];

        buf[0] = 0;
        buf[1] = (byte)UdpPacketType.Terminate;

        BinaryPrimitives.WriteInt32BigEndian(buf.Slice(2), connectCode);
        BinaryPrimitives.WriteInt16BigEndian(buf.Slice(6), (short)reason);

        PhysicalSend(buf, 8, true);
    }

    public bool Send(UdpChannel channel, Span<byte> data)
    {
        lock (_guard)
        {
            Debug.Assert(data.Length >= 0);

            if (Status != Status.Connected)
                return false;

            if (data.IsEmpty)
                return false;

            if (data[0] == 0)
            {
                Span<byte> hold = [0];
                return InternalSend(channel, hold, hold.Length, data, data.Length);
            }

            return InternalSend(channel, data, data.Length);
        }
    }

    public bool Send(UdpChannel channel, LogicalPacket packet)
    {
        lock (_guard)
        {
            if (Status != Status.Connected)
                return false;

            var dataLen = packet.GetDataLen();
            if (dataLen == 0)
                return false;

            var data = packet.GetDataPtr();
            if (data[0] == 0)
            {
                Span<byte> hold = [0];
                return InternalSend(channel, hold, hold.Length, data, dataLen);
            }

            return InternalSend(channel, data, dataLen);
        }
    }

    private bool InternalSend(UdpChannel channel, Span<byte> data, int dataLen, Span<byte> data2 = default, int dataLen2 = 0)
    {
        Debug.Assert(channel >= 0 && channel < UdpChannel.Count);
        Debug.Assert(Status != Status.Negotiating);

        UdpManager.IncrementApplicationPacketsSent();
        ConnectionStats.ApplicationPacketsSent++;

        var totalDataLen = dataLen + dataLen2;

        var rawDataBytesMax = ConnectionConfig.MaxRawPacketSize - ConnectionConfig.CrcBytes - EncryptExpansionBytes;

        if ((channel is UdpChannel.Unreliable or UdpChannel.UnreliableUnbuffered) && totalDataLen > rawDataBytesMax)
            channel = UdpChannel.Reliable1;
        else if ((channel is UdpChannel.Ordered or UdpChannel.OrderedUnbuffered) && totalDataLen > rawDataBytesMax - Constants.UdpPacketOrderedSize)
            channel = UdpChannel.Reliable1;

        switch (channel)
        {
            case UdpChannel.Unreliable:
                BufferedSend(data, dataLen, data2, dataLen2, false);
                return true;

            case UdpChannel.UnreliableUnbuffered:
                {
                    var tempBuffer = new byte[Constants.HardMaxRawPacketSize];

                    var bufPtr = tempBuffer.AsSpan();

                    data.CopyTo(bufPtr);

                    if (!data2.IsEmpty)
                        data2.CopyTo(bufPtr.Slice(data.Length));

                    PhysicalSend(bufPtr, totalDataLen, true);

                    return true;
                }

            case UdpChannel.Ordered:
                {
                    var tempBuffer = new byte[Constants.HardMaxRawPacketSize];

                    var bufPtr = tempBuffer.AsSpan();

                    bufPtr[0] = 0;
                    bufPtr[1] = (byte)UdpPacketType.Ordered;

                    BinaryPrimitives.WriteUInt16BigEndian(bufPtr.Slice(2), (ushort)++OrderedCountOutgoing);

                    data.CopyTo(bufPtr.Slice(4));

                    if (!data2.IsEmpty)
                        data2.CopyTo(bufPtr.Slice(4 + data.Length));

                    BufferedSend(bufPtr, totalDataLen + 4, null, 0, true);

                    return true;
                }

            case UdpChannel.OrderedUnbuffered:
                {
                    var tempBuffer = new byte[Constants.HardMaxRawPacketSize];

                    var bufPtr = tempBuffer.AsSpan();

                    bufPtr[0] = 0;
                    bufPtr[1] = (byte)UdpPacketType.Ordered2;

                    BinaryPrimitives.WriteUInt16BigEndian(bufPtr.Slice(2), (ushort)++OrderedCountOutgoing2);

                    data.CopyTo(bufPtr.Slice(4));

                    if (!data2.IsEmpty)
                        data2.CopyTo(bufPtr.Slice(data.Length + 4));

                    PhysicalSend(bufPtr, totalDataLen + 4, true);

                    return true;
                }

            case UdpChannel.Reliable1:
            case UdpChannel.Reliable2:
            case UdpChannel.Reliable3:
            case UdpChannel.Reliable4:
                {
                    var num = channel - UdpChannel.Reliable1;

                    if (Channel[num] is null)
                        Channel[num] = new UdpReliableChannel(num, this, UdpManager.Params.Reliable[num]);

                    Channel[num].Send(data, dataLen, data2, dataLen2);

                    return true;
                }
        }

        return false;
    }

    private void PingStatReset()
    {
        lock (_guard)
        {
            LastClockSyncTime = 0;

            SyncStatMasterFixupTime = 0;
            SyncStatMasterRoundTime = 0;
            SyncStatLow = 0;
            SyncStatHigh = 0;
            SyncStatLast = 0;
            SyncStatTotal = 0;
            SyncStatCount = 0;
            ConnectionStats.AveragePingTime = 0;
            ConnectionStats.HighPingTime = 0;
            ConnectionStats.LowPingTime = 0;
            ConnectionStats.LastPingTime = 0;
            ConnectionStats.MasterPingTime = 0;
        }
    }

    public void GetStats(out UdpConnectionStatistics stats)
    {
        lock (_guard)
        {
            stats = ConnectionStats;

            stats.MasterPingAge = UdpManager.Params.ClockSyncDelay == 0 ? -1 : UdpManager.CachedClockElapsed(SyncStatMasterFixupTime);

            stats.PercentSentSuccess = 1.0f;
            stats.PercentReceivedSuccess = 1.0f;

            if (stats.SyncOurSent > 0)
                stats.PercentSentSuccess = stats.SyncTheirReceived / stats.SyncOurSent;

            if (stats.SyncTheirSent > 0)
                stats.PercentReceivedSuccess = stats.SyncOurReceived / stats.SyncTheirSent;

            stats.ReliableAveragePing = 0;

            if (Channel[0] is not null)
                stats.ReliableAveragePing = Channel[0].GetAveragePing();
        }
    }

    internal void ProcessRawPacket(Span<byte> data)
    {
        lock (_guard)
        {
            if (data.Length == 0)
            {
                PortUnreachable();
                return;
            }

            var reader = new PacketReader(data);

            if (!reader.TryRead(out byte zeroByte))
                return;

            if (!reader.TryRead(out UdpPacketType packetType))
                return;

            if (zeroByte != 0 || packetType != UdpPacketType.UnreachableConnection)
            {
                PortRemapRequestStartStamp = 0;
            }

            IcmpErrorRetryStartStamp = 0;

            LastReceiveTime = UdpManager.CachedClock;

            ConnectionStats.TotalPacketsReceived++;
            ConnectionStats.TotalBytesReceived += data.Length;

            if (zeroByte == 0 && packetType == UdpPacketType.KeepAlive)
            {
                return;
            }

            ScheduleTimeNow();

            if (data.Length < 1)
            {
                CallbackCorruptPacket(data, UdpCorruptionReason.ZeroLengthPacket);
                return;
            }

            if (zeroByte == 0 && IsNonEncryptPacket(packetType))
            {
                ProcessCookedPacket(data);
            }
            else
            {
                if (Status == Status.Negotiating)
                    return;

                var finalStart = data;
                var finalLen = data.Length;

                if (ConnectionConfig.CrcBytes > 0)
                {
                    if (finalLen < ConnectionConfig.CrcBytes)
                    {
                        CallbackCorruptPacket(data, UdpCorruptionReason.PacketShorterThanCrcBytes);
                        return;
                    }

                    var crcPtr = finalStart.Slice(finalLen - ConnectionConfig.CrcBytes);

                    var wantCrc = 0u;
                    var actualCrc = UdpMisc.Crc32(finalStart, finalLen - ConnectionConfig.CrcBytes, ConnectionConfig.EncryptCode);

                    switch (ConnectionConfig.CrcBytes)
                    {
                        case 1:
                            wantCrc = crcPtr[0];
                            actualCrc &= 0xff;
                            break;

                        case 2:
                            wantCrc = BinaryPrimitives.ReadUInt16BigEndian(crcPtr);
                            actualCrc &= 0xffff;
                            break;

                        case 3:
                            wantCrc = UdpMisc.GetValue24(crcPtr);
                            actualCrc &= 0xffffff;
                            break;

                        case 4:
                            wantCrc = BinaryPrimitives.ReadUInt32BigEndian(crcPtr);
                            break;
                    }

                    if (wantCrc != actualCrc)
                    {
                        ConnectionStats.CrcRejectedPackets++;

                        UdpManager.IncrementCrcRejectedPackets();
                        UdpManager.CallbackCrcReject(this, data);

                        return;
                    }

                    finalLen -= ConnectionConfig.CrcBytes;
                }

                for (var i = Constants.EncryptPasses - 1; i >= 0; i--)
                {
                    if (ConnectionConfig.EncryptMethod[i] == EncryptMethod.None)
                        continue;

                    var decryptPtr = _tempDecryptBuffer[i].AsSpan();

                    decryptPtr[0] = finalStart[0];

                    if (finalStart[0] == 0)
                    {
                        if (finalLen < 2)
                        {
                            CallbackCorruptPacket(data, UdpCorruptionReason.InternalPacketTooShort);
                            return;
                        }

                        decryptPtr[1] = finalStart[1];

                        var len = DecryptFunction[i](decryptPtr.Slice(2), finalStart.Slice(2, finalLen - 2));

                        if (len == -1)
                        {
                            CallbackCorruptPacket(data, UdpCorruptionReason.DecryptFailed);
                            return;
                        }

                        finalLen = len + 2;
                    }
                    else
                    {
                        var len = DecryptFunction[i](decryptPtr.Slice(1), finalStart.Slice(1, finalLen - 1));

                        if (len == -1)
                        {
                            CallbackCorruptPacket(data, UdpCorruptionReason.DecryptFailed);
                            return;
                        }

                        finalLen = len + 1;
                    }

                    finalStart = _tempDecryptBuffer[i];
                }

                ProcessCookedPacket(finalStart.Slice(0, finalLen));
            }
        }
    }

    internal void CallbackRoutePacket(Span<byte> data)
    {
        if (Status != Status.Connected)
            return;

        UdpManager.IncrementApplicationPacketsReceived();

        ConnectionStats.ApplicationPacketsReceived++;

        UdpManager.CallbackRoutePacket(this, data);
    }

    internal void CallbackCorruptPacket(Span<byte> data, UdpCorruptionReason reason)
    {
        if (Status != Status.Connected)
            return;

        ConnectionStats.CorruptPacketErrors++;
        UdpManager.IncrementCorruptPacketErrors();

        UdpManager.CallbackPacketCorrupt(this, data, reason);

        InternalDisconnect(0, DisconnectReason.CorruptPacket);
    }

    internal void ProcessCookedPacket(Span<byte> data)
    {
        var reader = new PacketReader(data);

        if (!reader.TryRead(out byte zeroByte))
            return;

        if (zeroByte != 0 || data.Length <= 1)
        {
            CallbackRoutePacket(data);
            return;
        }

        if (!reader.TryRead(out byte packetType))
            return;

        switch ((UdpPacketType)packetType)
        {
            case UdpPacketType.Connect:
                {
                    if (!reader.TryReadInt32(out var otherSideProtocolVersion))
                        return;

                    if (!reader.TryReadInt32(out var connectCode))
                        return;

                    if (!reader.TryReadInt32(out var maxRawPacketSize))
                        return;

                    var otherSideProtocolName = string.Empty;

                    if (otherSideProtocolVersion > 2 && data.Length > 14)
                    {
                        for (var i = 0; i < 32; i++)
                        {
                            if (!reader.TryRead(out byte protocolChar) || protocolChar == 0)
                                break;

                            otherSideProtocolName += (char)protocolChar;
                        }
                    }

                    if (Status == Status.Negotiating)
                    {

                        SendTerminatePacket(connectCode, ConnectCode == connectCode
                            ? DisconnectReason.ConnectingToSelf
                            : DisconnectReason.MutualConnectError);
                    }
                    else if (ConnectCode == connectCode)
                    {
                        OtherSideProtocolVersion = otherSideProtocolVersion;

                        if (!string.IsNullOrEmpty(otherSideProtocolName))
                            OtherSideProtocolName = otherSideProtocolName;

                        ConnectionConfig.MaxRawPacketSize = Math.Min(maxRawPacketSize, ConnectionConfig.MaxRawPacketSize);

                        Span<byte> buf = stackalloc byte[21];

                        buf[0] = 0;
                        buf[1] = (byte)UdpPacketType.Confirm;
                        BinaryPrimitives.WriteInt32BigEndian(buf.Slice(2), ConnectCode);
                        BinaryPrimitives.WriteInt32BigEndian(buf.Slice(6), ConnectionConfig.EncryptCode);
                        buf[10] = ConnectionConfig.CrcBytes;

                        for (var i = 0; i < Constants.EncryptPasses; i++)
                            buf[11 + i] = (byte)ConnectionConfig.EncryptMethod[i];

                        BinaryPrimitives.WriteInt32BigEndian(buf.Slice(13), ConnectionConfig.MaxRawPacketSize);
                        BinaryPrimitives.WriteInt32BigEndian(buf.Slice(17), Constants.ProtocolVersion);

                        RawSend(buf, buf.Length);

                        if (!string.IsNullOrEmpty(UdpManager.Params.ProtocolName) && !string.Equals(UdpManager.Params.ProtocolName, OtherSideProtocolName))
                            InternalDisconnect(0, DisconnectReason.OtherProtocolName);
                    }
                    else
                    {
                        SendTerminatePacket(0, DisconnectReason.NewConnectionAttempt);
                    }
                }
                break;

            case UdpPacketType.Confirm:
                {
                    var config = new Configuration();

                    var otherSideProtocolVersion = 0;

                    if (!reader.TryReadInt32(out var connectCode))
                        return;

                    if (!reader.TryReadInt32(out config.EncryptCode))
                        return;

                    if (!reader.TryRead(out config.CrcBytes))
                        return;

                    for (var i = 0; i < Constants.EncryptPasses; i++)
                    {
                        if (!reader.TryRead(out byte encryptMethod))
                            return;

                        config.EncryptMethod[i] = (EncryptMethod)encryptMethod;
                    }

                    if (!reader.TryReadInt32(out config.MaxRawPacketSize))
                        return;

                    if (reader.RemainingLength > 0)
                    {
                        if (!reader.TryReadInt32(out otherSideProtocolVersion))
                            return;
                    }

                    if (Status == Status.Negotiating && ConnectCode == connectCode)
                    {
                        ConnectionConfig = config;
                        OtherSideProtocolVersion = otherSideProtocolVersion;
                        SetupEncryptModel();
                        Status = Status.Connected;
                        UdpManager.CallbackConnectComplete(this);
                    }
                }
                break;

            case UdpPacketType.RequestRemap:
                break;

            case UdpPacketType.ZeroEscape:
                CallbackRoutePacket(data.Slice(1));
                break;

            case UdpPacketType.Ordered:
                {
                    if (!reader.TryReadUInt16(out var orderedStamp))
                        return;

                    var diff = orderedStamp - OrderedStampLast;

                    if (diff <= 0)
                        diff += 0x10000;

                    if (diff < 30000)
                    {
                        OrderedStampLast = orderedStamp;
                        CallbackRoutePacket(data.Slice(Constants.UdpPacketOrderedSize));
                    }
                    else
                    {
                        ConnectionStats.OrderRejectedPackets++;
                        UdpManager.IncrementOrderRejectedPackets();
                    }
                }
                break;

            case UdpPacketType.Ordered2:
                {
                    if (!reader.TryReadUInt16(out var orderedStamp))
                        return;

                    var diff = orderedStamp - OrderedStampLast2;

                    if (diff <= 0)
                        diff += 0x10000;

                    if (diff < 30000)
                    {
                        OrderedStampLast2 = orderedStamp;
                        CallbackRoutePacket(data.Slice(Constants.UdpPacketOrderedSize));
                    }
                    else
                    {
                        ConnectionStats.OrderRejectedPackets++;
                        UdpManager.IncrementOrderRejectedPackets();
                    }
                }
                break;

            case UdpPacketType.Terminate:
                {
                    if (!reader.TryReadInt32(out var connectCode))
                        return;

                    if (reader.RemainingLength > 0)
                    {
                        if (!reader.TryReadInt16(out var otherSideDisconnectReason))
                            return;

                        OtherSideDisconnectReason = (DisconnectReason)otherSideDisconnectReason;
                    }

                    if (ConnectCode == connectCode)
                    {
                        SilentDisconnect = true;
                        InternalDisconnect(0, DisconnectReason.OtherSideTerminated);
                        return;
                    }
                }
                break;

            case UdpPacketType.UnreachableConnection:
                {
                    if (UdpManager.Params.AllowPortRemapping)
                    {
                        if (PortRemapRequestStartStamp == 0)
                        {
                            PortRemapRequestStartStamp = UdpManager.CachedClock;
                        }

                        if (UdpManager.CachedClockElapsed(PortRemapRequestStartStamp) < Constants.MaximumTimeAllowedForPortRemapping)
                        {
                            Span<byte> buf = stackalloc byte[21];

                            buf[0] = 0;
                            buf[1] = (byte)UdpPacketType.RequestRemap;
                            BinaryPrimitives.WriteInt32BigEndian(buf.Slice(2), ConnectCode);
                            BinaryPrimitives.WriteInt32BigEndian(buf.Slice(6), ConnectionConfig.EncryptCode);

                            RawSend(buf, buf.Length);

                            break;
                        }
                    }

                    InternalDisconnect(0, DisconnectReason.UnreachableConnection);
                }
                return;

            case UdpPacketType.Multi:
                {
                    var ptr = 2;
                    var endPtr = data.Length;

                    while (ptr < endPtr)
                    {
                        var len = data[ptr++];

                        var nextPtr = ptr + len;

                        if (nextPtr > endPtr)
                        {
                            CallbackCorruptPacket(data, UdpCorruptionReason.MisformattedGroup);
                            return;
                        }

                        ProcessCookedPacket(data.Slice(ptr, len));

                        ptr = nextPtr;
                    }

                    Debug.Assert(ptr == endPtr);
                }
                break;

            case UdpPacketType.ClockSync:
                {
                    UdpPacketClockSync pp;

                    if (!reader.TryReadUInt16(out pp.TimeStamp) ||
                        !reader.TryReadUInt32(out pp.MasterPingTime) ||
                        !reader.TryReadUInt32(out pp.AveragePingTime) ||
                        !reader.TryReadUInt32(out pp.LowPingTime) ||
                        !reader.TryReadUInt32(out pp.HighPingTime) ||
                        !reader.TryReadUInt32(out pp.LastPingTime) ||
                        !reader.TryReadInt64(out pp.OurSent) ||
                        !reader.TryReadInt64(out pp.OurReceived))
                        return;

                    ConnectionStats.AveragePingTime = pp.AveragePingTime;
                    ConnectionStats.HighPingTime = pp.HighPingTime;
                    ConnectionStats.LowPingTime = pp.LowPingTime;
                    ConnectionStats.LastPingTime = pp.LastPingTime;
                    ConnectionStats.MasterPingTime = pp.MasterPingTime;
                    ConnectionStats.SyncOurReceived = ConnectionStats.TotalPacketsReceived;
                    ConnectionStats.SyncOurSent = ConnectionStats.TotalPacketsSent;
                    ConnectionStats.SyncTheirReceived = pp.OurReceived;
                    ConnectionStats.SyncTheirSent = pp.OurSent;

                    if (UdpManager.ProcessingInducedLag > 1000)
                        break;

                    Span<byte> buf = stackalloc byte[40 + 4];

                    buf[0] = 0;
                    buf[1] = (byte)UdpPacketType.ClockReflect;

                    BinaryPrimitives.WriteUInt16BigEndian(buf.Slice(2), pp.TimeStamp);
                    BinaryPrimitives.WriteUInt32BigEndian(buf.Slice(4), UdpManager.LocalSyncStampLong());
                    BinaryPrimitives.WriteInt64BigEndian(buf.Slice(8), pp.OurSent);
                    BinaryPrimitives.WriteInt64BigEndian(buf.Slice(16), pp.OurReceived);
                    BinaryPrimitives.WriteInt64BigEndian(buf.Slice(24), ConnectionStats.TotalPacketsSent);
                    BinaryPrimitives.WriteInt64BigEndian(buf.Slice(32), ConnectionStats.TotalPacketsReceived);

                    PhysicalSend(buf, 40, true);
                }
                break;

            case UdpPacketType.ClockReflect:
                {
                    UdpPacketClockReflect pp;

                    if (!reader.TryReadUInt16(out pp.TimeStamp) ||
                        !reader.TryReadUInt32(out pp.ServerSyncStampLong) ||
                        !reader.TryReadInt64(out pp.YourSent) ||
                        !reader.TryReadInt64(out pp.YourReceived) ||
                        !reader.TryReadInt64(out pp.OurSent) ||
                        !reader.TryReadInt64(out pp.OurReceived))
                        return;

                    ConnectionStats.SyncOurReceived = pp.YourReceived;
                    ConnectionStats.SyncOurSent = pp.YourSent;
                    ConnectionStats.SyncTheirReceived = pp.OurReceived;
                    ConnectionStats.SyncTheirSent = pp.OurSent;

                    if (UdpManager.ProcessingInducedLag > 1000)
                        break;

                    var curStamp = UdpManager.LocalSyncStampShort();
                    var roundTime = UdpMisc.SyncStampShortDeltaTime(pp.TimeStamp, curStamp);

                    SyncStatCount++;
                    SyncStatTotal += roundTime;

                    if (SyncStatLow == 0 || roundTime < SyncStatLow)
                        SyncStatLow = roundTime;

                    if (roundTime > SyncStatHigh)
                        SyncStatHigh = roundTime;

                    SyncStatLast = roundTime;

                    var elapsed = UdpManager.CachedClockElapsed(SyncStatMasterFixupTime);

                    if (roundTime <= SyncStatMasterRoundTime + 20 || elapsed > 120000)
                    {
                        if (roundTime < SyncStatMasterRoundTime * 2 || elapsed > 240000)
                        {
                            SyncTimeDelta = pp.ServerSyncStampLong - UdpManager.LocalSyncStampLong() + (uint)(roundTime / 2);
                            SyncStatMasterFixupTime = UdpManager.CachedClock;
                            SyncStatMasterRoundTime = roundTime;
                        }
                    }

                    ConnectionStats.AveragePingTime = (SyncStatCount > 0) ? (SyncStatTotal / SyncStatCount) : 0u;
                    ConnectionStats.HighPingTime = SyncStatHigh;
                    ConnectionStats.LowPingTime = SyncStatLow;
                    ConnectionStats.LastPingTime = roundTime;
                    ConnectionStats.MasterPingTime = SyncStatMasterRoundTime;
                }
                break;

            case UdpPacketType.KeepAlive:
                break;

            case UdpPacketType.Reliable1:
            case UdpPacketType.Reliable2:
            case UdpPacketType.Reliable3:
            case UdpPacketType.Reliable4:
            case UdpPacketType.Fragment1:
            case UdpPacketType.Fragment2:
            case UdpPacketType.Fragment3:
            case UdpPacketType.Fragment4:
                {
                    var num = (packetType - (byte)UdpPacketType.Reliable1) % Constants.ReliableChannelCount;

                    if (Channel[num] is null)
                        Channel[num] = new UdpReliableChannel(num, this, UdpManager.Params.Reliable[num]);

                    Channel[num].ReliablePacket(data);
                }
                break;

            case UdpPacketType.Ack1:
            case UdpPacketType.Ack2:
            case UdpPacketType.Ack3:
            case UdpPacketType.Ack4:
                {
                    var num = packetType - (byte)UdpPacketType.Ack1;

                    Channel[num]?.AckPacket(data);
                }
                break;

            case UdpPacketType.AckAll1:
            case UdpPacketType.AckAll2:
            case UdpPacketType.AckAll3:
            case UdpPacketType.AckAll4:
                {
                    var num = packetType - (byte)UdpPacketType.AckAll1;

                    Channel[num]?.AckAllPacket(data);
                }
                break;

            case UdpPacketType.Group:
                {
                    var ptr = 2;
                    var endPtr = data.Length;

                    while (ptr < endPtr)
                    {
                        ptr += UdpMisc.GetVariableValue(data.Slice(ptr), out var len);

                        if (ptr > endPtr || len > endPtr - ptr)
                        {
                            CallbackCorruptPacket(data, UdpCorruptionReason.MisformattedGroup);
                            return;
                        }

                        ProcessCookedPacket(data.Slice(ptr, len));

                        ptr += len;
                    }
                }
                break;
        }
    }

    public void GiveTime()
    {
        lock (_guard)
        {
            GettingTime = true;

            InternalGiveTime();

            GettingTime = false;
        }
    }

    private void InternalGiveTime()
    {
        var nextSchedule = 10 * 60 * 1000L;

        ConnectionStats.Iterations++;

        if (FlaggedPortUnreachable)
        {
            FlaggedPortUnreachable = false;
            PortUnreachable();
        }

        switch (Status)
        {
            case Status.Negotiating:
                {
                    if (ConnectAttemptTimeout > 0 && ConnectionAge() > ConnectAttemptTimeout)
                    {
                        InternalDisconnect(0, DisconnectReason.ConnectFail);
                        return;
                    }

                    var elapsed = UdpManager.CachedClockElapsed(LastSendTime);

                    if (elapsed >= UdpManager.Params.ConnectAttemptDelay)
                    {

                        var protocolNameBytes = Encoding.ASCII.GetBytes(UdpManager.Params.ProtocolName);

                        Span<byte> buf = stackalloc byte[14 + protocolNameBytes.Length + 1];

                        buf[0] = 0;
                        buf[1] = (byte)UdpPacketType.Connect;
                        BinaryPrimitives.WriteInt32BigEndian(buf.Slice(2), Constants.ProtocolVersion);
                        BinaryPrimitives.WriteInt32BigEndian(buf.Slice(6), ConnectCode);
                        BinaryPrimitives.WriteInt32BigEndian(buf.Slice(10), UdpManager.Params.MaxRawPacketSize);
                        protocolNameBytes.CopyTo(buf.Slice(14));
                        buf[^1] = 0;

                        RawSend(buf, buf.Length);

                        elapsed = 0;
                    }

                    nextSchedule = Math.Min(nextSchedule, UdpManager.Params.ConnectAttemptDelay - elapsed);
                }
                break;

            case Status.Connected:
            case Status.DisconnectPending:
                {

                    if (UdpManager.Params.ClockSyncDelay > 0)
                    {

                        var elapsed = UdpManager.CachedClockElapsed(LastClockSyncTime);

                        if (elapsed > UdpManager.Params.ClockSyncDelay
                            || (SyncStatMasterRoundTime > 3000 && elapsed > 2000)
                            || (SyncStatMasterRoundTime > 1000 && elapsed > 5000)
                            || (SyncStatCount < 2 && elapsed > 10000))
                        {

                            var averagePing = (SyncStatCount > 0) ? (SyncStatTotal / SyncStatCount) : 0;

                            Span<byte> buf = stackalloc byte[40 + 4];

                            buf[0] = 0;
                            buf[1] = (byte)UdpPacketType.ClockSync;
                            BinaryPrimitives.WriteUInt16BigEndian(buf.Slice(2), UdpManager.LocalSyncStampShort());
                            BinaryPrimitives.WriteUInt32BigEndian(buf.Slice(4), SyncStatMasterRoundTime);
                            BinaryPrimitives.WriteUInt32BigEndian(buf.Slice(8), averagePing);
                            BinaryPrimitives.WriteUInt32BigEndian(buf.Slice(12), SyncStatLow);
                            BinaryPrimitives.WriteUInt32BigEndian(buf.Slice(16), SyncStatHigh);
                            BinaryPrimitives.WriteUInt32BigEndian(buf.Slice(20), SyncStatLast);
                            BinaryPrimitives.WriteInt64BigEndian(buf.Slice(24), ConnectionStats.TotalPacketsSent + 1);
                            BinaryPrimitives.WriteInt64BigEndian(buf.Slice(32), ConnectionStats.TotalPacketsReceived);

                            PhysicalSend(buf, 40, true);

                            LastClockSyncTime = UdpManager.CachedClock;

                            elapsed = 0;
                        }

                        nextSchedule = Math.Min(nextSchedule, UdpManager.Params.ClockSyncDelay - elapsed);
                    }

                    var totalPendingBytes = 0;

                    for (var i = 0; i < Constants.ReliableChannelCount; i++)
                    {
                        if (Channel[i] is null)
                            continue;

                        totalPendingBytes += Channel[i].TotalPendingBytes();

                        var myNext = Channel[i].GiveTime();

                        nextSchedule = Math.Min(nextSchedule, myNext);
                    }

                    if (UdpManager.Params.ReliableOverflowBytes != 0 && totalPendingBytes >= UdpManager.Params.ReliableOverflowBytes)
                    {
                        InternalDisconnect(0, DisconnectReason.ReliableOverflow);
                        return;
                    }

                    if (MultiBufferOffset > 2)
                    {
                        var elapsed = UdpManager.CachedClockElapsed(DataHoldTime);

                        if (elapsed >= UdpManager.Params.MaxDataHoldTime)
                            FlushMultiBuffer();
                        else
                            nextSchedule = Math.Min(nextSchedule, UdpManager.Params.MaxDataHoldTime - elapsed);
                    }

                    if (KeepAliveDelay > 0)
                    {
                        var elapsed = UdpManager.CachedClockElapsed(LastSendTime);

                        if (elapsed >= KeepAliveDelay)
                        {
                            Span<byte> buf = stackalloc byte[2 + 4];

                            buf[0] = 0;
                            buf[1] = (byte)UdpPacketType.KeepAlive;

                            PhysicalSend(buf, 2, true);

                            elapsed = 0;
                        }

                        nextSchedule = Math.Min(nextSchedule, KeepAliveDelay - elapsed);
                    }

                    if (UdpManager.Params.PortAliveDelay > 0)
                    {
                        var portElapsed = UdpManager.CachedClockElapsed(LastPortAliveTime);

                        if (portElapsed >= UdpManager.Params.PortAliveDelay)
                        {
                            LastPortAliveTime = UdpManager.CachedClock;
                            UdpManager.SendPortAlive(SocketAddress);
                            portElapsed = 0;
                        }

                        nextSchedule = Math.Min(nextSchedule, UdpManager.Params.PortAliveDelay - portElapsed);
                    }

                    if (Status == Status.DisconnectPending)
                    {
                        var timeLeft = DisconnectFlushTimeout - UdpManager.CachedClockElapsed(DisconnectFlushStamp);

                        if (timeLeft < 0 || TotalPendingBytes() == 0)
                        {
                            InternalDisconnect(0, DisconnectReason);
                            return;
                        }
                        else
                        {
                            nextSchedule = Math.Min(nextSchedule, timeLeft);
                        }
                    }

                    if (NoDataTimeout > 0)
                    {
                        var lrt = LastReceive();

                        if (lrt >= NoDataTimeout)
                        {
                            InternalDisconnect(0, DisconnectReason.Timeout);
                            return;
                        }
                        else
                        {
                            nextSchedule = Math.Min(nextSchedule, NoDataTimeout - lrt);
                        }
                    }
                }
                break;
        }

        if (nextSchedule < 0)
            nextSchedule = 0;

        UdpManager.SetPriority(this, UdpManager.CachedClock + nextSchedule + 5);
    }

    private int TotalPendingBytes()
    {
        lock (_guard)
        {
            var total = 0;

            for (var i = 0; i < Constants.ReliableChannelCount; i++)
            {
                if (Channel[i] is not null)
                    total += Channel[i].TotalPendingBytes();
            }

            return total;
        }
    }

    private void RawSend(ReadOnlySpan<byte> data, int dataLen)
    {

        UdpManager.ActualSend(data, dataLen, SocketAddress);

        ConnectionStats.TotalPacketsSent++;
        ConnectionStats.TotalBytesSent += dataLen;

        LastPortAliveTime = LastSendTime = UdpManager.CachedClock;

        ScheduleTimeNow();
    }

    private void PhysicalSend(Span<byte> data, int dataLen, bool appendAllowed)
    {
        if (Status != Status.Connected && Status != Status.DisconnectPending)
            return;

        var finalStart = data;
        var finalLen = dataLen;

        for (var i = 0; i < Constants.EncryptPasses; i++)
        {
            if (ConnectionConfig.EncryptMethod[i] == EncryptMethod.None)
                continue;

            var destStart = _tempEncryptBuffer[i].AsSpan();

            var destPtr = destStart;

            destPtr[0] = finalStart[0];

            if (finalStart[0] == 0)
            {

                destPtr[1] = finalStart[1];

                var len = EncryptFunction[i](destPtr.Slice(2), finalStart.Slice(2, finalLen - 2));

                if (len == -1)
                    return;

                finalLen = len + 2;
            }
            else
            {
                var len = EncryptFunction[i](destPtr.Slice(1), finalStart.Slice(1, finalLen - 1));

                if (len == -1)
                    return;

                finalLen = len + 1;
            }

            finalStart = destStart;

            appendAllowed = true;
        }

        if (ConnectionConfig.CrcBytes > 0)
        {
            if (!appendAllowed)
            {
                finalStart.Slice(0, finalLen).CopyTo(_tempEncryptBuffer[0]);
                finalStart = _tempEncryptBuffer[0];
            }

            var crc = UdpMisc.Crc32(finalStart, finalLen, ConnectionConfig.EncryptCode);

            var crcPtr = finalStart.Slice(finalLen);

            switch (ConnectionConfig.CrcBytes)
            {
                case 1:
                    crcPtr[0] = (byte)crc;
                    break;

                case 2:
                    BinaryPrimitives.WriteUInt16BigEndian(crcPtr, (ushort)crc);
                    break;

                case 3:
                    UdpMisc.PutValue24(crcPtr, crc);
                    break;

                case 4:
                    BinaryPrimitives.WriteUInt32BigEndian(crcPtr, crc);
                    break;
            }

            finalLen += ConnectionConfig.CrcBytes;
        }

        RawSend(finalStart, finalLen);
    }

    internal Span<byte> BufferedSend(Span<byte> data, int dataLen, Span<byte> data2, int dataLen2, bool appendAllowed)
    {
        var used = MultiBufferOffset;
        var ptr = MultiBufferData.AsSpan();

        var actualMaxDataHoldSize = Math.Min(UdpManager.Params.MaxDataHoldSize, ConnectionConfig.MaxRawPacketSize);

        var totalDataLen = dataLen + dataLen2;

        if (totalDataLen > 255 || (totalDataLen + 3) > actualMaxDataHoldSize)
        {

            if (used > 2)
                FlushMultiBuffer();

            if (!data2.IsEmpty)
            {
                data.Slice(0, dataLen).CopyTo(ptr);
                data2.Slice(0, dataLen2).CopyTo(ptr.Slice(dataLen));

                PhysicalSend(ptr, totalDataLen, true);
            }
            else
            {
                PhysicalSend(data, dataLen, appendAllowed);
            }

            return null;
        }

        if (used + totalDataLen + 1 > (ConnectionConfig.MaxRawPacketSize - ConnectionConfig.CrcBytes - EncryptExpansionBytes))
        {
            FlushMultiBuffer();
            used = 0;
        }

        if (used == 0)
        {

            ptr[MultiBufferOffset++] = 0;
            ptr[MultiBufferOffset++] = (byte)UdpPacketType.Multi;

            DataHoldTime = UdpManager.CachedClock;

            ScheduleTimeNow();
        }

        ptr[MultiBufferOffset++] = (byte)totalDataLen;

        var placementPtr = ptr.Slice(MultiBufferOffset);

        data.Slice(0, dataLen).CopyTo(ptr.Slice(MultiBufferOffset));

        MultiBufferOffset += dataLen;

        if (!data2.IsEmpty)
        {
            data2.Slice(0, dataLen2).CopyTo(ptr.Slice(MultiBufferOffset));
            MultiBufferOffset += dataLen2;
        }

        if (MultiBufferOffset >= actualMaxDataHoldSize)
        {
            FlushMultiBuffer();
            placementPtr = null;
        }

        return placementPtr;
    }

    private void FlushMultiBuffer()
    {
        lock (_guard)
        {
            var len = MultiBufferOffset;
            var ptr = MultiBufferData.AsSpan();

            if (len > 2)
            {
                if (ptr[2] + 3 == len)
                {
                    PhysicalSend(ptr.Slice(3), len - 3, true);
                }
                else
                {
                    PhysicalSend(ptr, len, true);
                }

                for (var i = 0; i < Constants.ReliableChannelCount; i++)
                    Channel[i]?.ClearBufferedAck();
            }

            MultiBufferOffset = 0;
        }
    }

    public void Disconnect(int flushTimeout = 0)
    {
        lock (_guard)
        {
            InternalDisconnect(flushTimeout, DisconnectReason.Application);
        }
    }

    private UdpClockStamp LastReceive(UdpClockStamp useStamp)
    {
        lock (_guard)
        {
            return UdpMisc.ClockDiff(LastReceiveTime, useStamp);
        }
    }

    private UdpClockStamp LastReceive()
    {
        lock (_guard)
        {
            return UdpManager.CachedClockElapsed(LastReceiveTime);
        }
    }

    private bool IsNonEncryptPacket(UdpPacketType packetType)
    {
        return packetType
            is UdpPacketType.Connect
            or UdpPacketType.Confirm
            or UdpPacketType.UnreachableConnection
            or UdpPacketType.RequestRemap
            or UdpPacketType.Unknown
            or UdpPacketType.ServerStatus;
    }

    private UdpClockStamp ConnectionAge()
    {
        lock (_guard)
        {
            return UdpManager.CachedClockElapsed(ConnectionCreateTime);
        }
    }

    internal void ScheduleTimeNow()
    {
        if (!GettingTime)
        {
            UdpManager.SetPriority(this, 0);
        }
    }

    #region Encryption

    private int EncryptNone(Span<byte> destData, Span<byte> sourceData)
    {
        return sourceData.TryCopyTo(destData) ? sourceData.Length : -1;
    }

    private int DecryptNone(Span<byte> destData, Span<byte> sourceData)
    {
        return sourceData.TryCopyTo(destData) ? sourceData.Length : -1;
    }

    protected virtual int EncryptUserSupplied(Span<byte> destData, Span<byte> sourceData)
    {
        return sourceData.TryCopyTo(destData) ? sourceData.Length : -1;
    }

    protected virtual int DecryptUserSupplied(Span<byte> destData, Span<byte> sourceData)
    {
        return sourceData.TryCopyTo(destData) ? sourceData.Length : -1;
    }

    protected virtual int EncryptUserSupplied2(Span<byte> destData, Span<byte> sourceData)
    {
        return sourceData.TryCopyTo(destData) ? sourceData.Length : -1;
    }

    protected virtual int DecryptUserSupplied2(Span<byte> destData, Span<byte> sourceData)
    {
        return sourceData.TryCopyTo(destData) ? sourceData.Length : -1;
    }

    private int EncryptXorBuffer(Span<byte> destData, Span<byte> sourceData)
    {
        if (_encryptXorBuffer is null)
            return -1;

        var destPtr = 0;
        var sourcePtr = 0;

        var encryptPtr = 0;
        var encryptBuffer = _encryptXorBuffer.AsSpan();

        var prev = ConnectionConfig.EncryptCode;

        while (sourcePtr + sizeof(int) <= sourceData.Length)
        {
            var hold = MemoryMarshal.Read<int>(sourceData.Slice(sourcePtr));
            var encrypt = MemoryMarshal.Read<int>(encryptBuffer.Slice(encryptPtr));
            var value = hold ^ encrypt ^ prev;

            MemoryMarshal.Write(destData.Slice(destPtr), value);

            prev = value;

            destPtr += sizeof(int);
            sourcePtr += sizeof(int);
            encryptPtr += sizeof(int);
        }

        while (sourcePtr != sourceData.Length)
            destData[destPtr++] = (byte)(sourceData[sourcePtr++] ^ _encryptXorBuffer[encryptPtr++]);

        return sourceData.Length;
    }

    private int DecryptXorBuffer(Span<byte> destData, Span<byte> sourceData)
    {
        if (_encryptXorBuffer is null)
            return -1;

        var destPtr = 0;
        var sourcePtr = 0;

        var encryptPtr = 0;
        var encryptBuffer = _encryptXorBuffer.AsSpan();

        var prev = ConnectionConfig.EncryptCode;

        while (sourcePtr + sizeof(int) <= sourceData.Length)
        {
            var hold = MemoryMarshal.Read<int>(sourceData.Slice(sourcePtr));
            var encrypt = MemoryMarshal.Read<int>(encryptBuffer.Slice(encryptPtr));

            MemoryMarshal.Write(destData.Slice(destPtr), hold ^ prev ^ encrypt);

            prev = hold;

            destPtr += sizeof(int);
            sourcePtr += sizeof(int);
            encryptPtr += sizeof(int);
        }

        while (sourcePtr != sourceData.Length)
            destData[destPtr++] = (byte)(sourceData[sourcePtr++] ^ _encryptXorBuffer[encryptPtr++]);

        return sourceData.Length;
    }

    private int EncryptXor(Span<byte> destData, Span<byte> sourceData)
    {
        var destPtr = 0;
        var sourcePtr = 0;

        var prev = ConnectionConfig.EncryptCode;

        while (sourcePtr + sizeof(int) <= sourceData.Length)
        {
            var hold = MemoryMarshal.Read<int>(sourceData.Slice(sourcePtr));
            var value = hold ^ prev;

            MemoryMarshal.Write(destData.Slice(destPtr), value);

            prev = value;

            destPtr += sizeof(int);
            sourcePtr += sizeof(int);
        }

        while (sourcePtr != sourceData.Length)
            destData[destPtr++] = (byte)(sourceData[sourcePtr++] ^ prev);

        return sourceData.Length;
    }

    private int DecryptXor(Span<byte> destData, Span<byte> sourceData)
    {
        var destPtr = 0;
        var sourcePtr = 0;

        var prev = ConnectionConfig.EncryptCode;

        while (sourcePtr + sizeof(int) <= sourceData.Length)
        {
            var hold = MemoryMarshal.Read<int>(sourceData.Slice(sourcePtr));

            MemoryMarshal.Write(destData.Slice(destPtr), hold ^ prev);

            prev = hold;

            destPtr += sizeof(int);
            sourcePtr += sizeof(int);
        }

        while (sourcePtr != sourceData.Length)
            destData[destPtr++] = (byte)(sourceData[sourcePtr++] ^ prev);

        return sourceData.Length;
    }

    private void SetupEncryptModel()
    {
        EncryptExpansionBytes = 0;

        for (var j = 0; j < Constants.EncryptPasses; j++)
        {
            switch (ConnectionConfig.EncryptMethod[j])
            {
                case EncryptMethod.None:
                    DecryptFunction[j] = DecryptNone;
                    EncryptFunction[j] = EncryptNone;
                    EncryptExpansionBytes += 0;
                    break;

                case EncryptMethod.UserSupplied:
                    DecryptFunction[j] = DecryptUserSupplied;
                    EncryptFunction[j] = EncryptUserSupplied;
                    EncryptExpansionBytes += UdpManager.Params.UserSuppliedEncryptExpansionBytes;
                    break;

                case EncryptMethod.UserSupplied2:
                    DecryptFunction[j] = DecryptUserSupplied2;
                    EncryptFunction[j] = EncryptUserSupplied2;
                    EncryptExpansionBytes += UdpManager.Params.UserSuppliedEncryptExpansionBytes2;
                    break;

                case EncryptMethod.XorBuffer:
                    {
                        DecryptFunction[j] = DecryptXorBuffer;
                        EncryptFunction[j] = EncryptXorBuffer;
                        EncryptExpansionBytes += 0;

                        if (_encryptXorBuffer is null)
                        {
                            var len = ((UdpManager.Params.MaxRawPacketSize + 1) / 4) * 4;

                            _encryptXorBuffer = new byte[len];

                            var seed = ConnectionConfig.EncryptCode;

                            for (var i = 0; i < len; i++)
                                _encryptXorBuffer[i] = (byte)UdpMisc.Random(ref seed);
                        }
                    }
                    break;

                case EncryptMethod.Xor:
                    {
                        DecryptFunction[j] = DecryptXor;
                        EncryptFunction[j] = EncryptXor;
                        EncryptExpansionBytes += 0;
                    }
                    break;
            }
        }
    }

    #endregion

    #region Handler

    public virtual void OnRoutePacket(Span<byte> data)
    {
    }

    public virtual void OnConnectComplete()
    {
    }

    public virtual void OnTerminated()
    {
    }

    public virtual void OnCrcReject(Span<byte> data)
    {
    }

    public virtual void OnPacketCorrupt(Span<byte> data, UdpCorruptionReason reason)
    {
    }

    #endregion

    public override string ToString()
    {
        return EndPoint.ToString();
    }
}
