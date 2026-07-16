namespace Sanctuary.UdpLibrary.Configuration;

public struct UdpReliableConfig
{
    // Maximum number of bytes that are allowed to be outstanding without an acknowledgement before more are sent.
    // 200000
    public int MaxOutstandingBytes = 200000;

    // Maximum number of physical reliable packets that are allowed to be outstanding.
    // default = 400
    public int MaxOutstandingPackets = 400;

    // Maximum number of incoming reliable packets it will queue for ordered delivery while waiting for the missing packet to arrive (should generally be same as MaxOutstandingPackets setting on other side).
    // default = 400
    public int MaxInstandingPackets = 400;

    // This is the size it should fragment large logical packets into.
    // default = 0 max allowed = MaxRawPacketSize
    public int FragmentSize = 0;

    // Maximum number of bytes to send per trickleRate period of time.
    // default = 0 max allowed = FragmentSize
    public int TrickleSize;

    // How often TrickleSize bytes are sent on the channel.
    // default = 0 no trickle control
    public int TrickleRate = 0;

    // Amount of additional time (in ms) above the average ack-time before a packet should be deemed lost and resent.
    // default = 300
    public int ResendDelayAdjust = 300;

    // Percent average ack-time it should use in calculating the resend delay.
    // default = 125 or 125%
    public int ResendDelayPercent = 125;

    // Maximum length of resend-delay that will ever be assigned to an outstanding packet.
    // default = 5000
    public int ResendDelayCap = 5000;

    // The minimum size to allow the congestion-window to shrink.
    // This defaults to 0, though internally it the implementation will never let the window get smaller than a single raw packet (512 bytes by default).
    // This setting is more intended to allow the application to set a higher minimum than that, effectively
    // allowing the application to tell the connection to refuse to slow itself down as much.
    public int CongestionWindowMinimum = 0;

    public int CongestionWindowMaximum;

    // The number of resend-accellerated packets in a frame that is severe enough to constitute a resetting of the flow control window.
    // Typically this number is set in extremely high bandwidth (LFN) situations where some small amount of packetloss should not reset the flow-control window.
    // default = 0, which means that even is a single lost packet will reset the window, which matches previously UdpLibrary and TCP like behavior.
    // Setting this too high can cause it to be unfriendly to other connections sharing the network.
    public int ToleranceLossCount = 0;

    // Whether incoming packets on this channel should be allowed to be delivered out of order.
    // default = false
    public bool OutOfOrder = false;

    // Whether the reliable-channel should attempt to coalesce data to reduce ack's needed.
    // default = true
    // Rarely change this to false.
    public bool Coalesce = true;

    // Whether ack-packets stuck into the low-level multi-buffer should be deduped.
    // default = true
    // Rarely change this to false.
    public bool AckDeduping = true;

    public UdpReliableConfig()
    {
    }
}