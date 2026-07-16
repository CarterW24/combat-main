namespace Sanctuary.UdpLibrary.Enumerations;

public enum UdpChannel : byte
{
    // Unreliable/unordered/buffered.
    Unreliable,

    // Unreliable/unordered/unbuffered.
    UnreliableUnbuffered,

    // Unreliable/ordered/buffered.
    Ordered,

    // Unreliable/ordered/unbuffered.
    OrderedUnbuffered,

    // Reliable (as per channel config).
    Reliable1,
    Reliable2,
    Reliable3,
    Reliable4,

    Count
}