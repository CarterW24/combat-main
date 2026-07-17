namespace Sanctuary.UdpLibrary.Enumerations;

public enum UdpChannel : byte
{
    Unreliable,

    UnreliableUnbuffered,

    Ordered,

    OrderedUnbuffered,

    Reliable1,
    Reliable2,
    Reliable3,
    Reliable4,

    Count
}
