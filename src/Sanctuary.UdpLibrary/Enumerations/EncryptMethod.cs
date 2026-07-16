namespace Sanctuary.UdpLibrary.Enumerations;


// Encryption methods are allowed to change the packet-size, so raw packet level compression
// would actually be implemented as a new encryption-method if needed
// the user-supplied method requires that the both ends of the connection have the user-supplied
// encrypt handler functions setup to correspond to each other.
public enum EncryptMethod : byte
{
    None,

    // Use the EncryptUserSupplied function.
    UserSupplied,

    // Use the EncryptUserSupplied2 function.
    UserSupplied2,

    // Slower xor method, but slightly more encrypted.
    XorBuffer,

    // Faster using less memory, slightly less well encrypted, use this one as first choice typically.
    Xor
}