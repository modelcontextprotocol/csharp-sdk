#if !NET9_0_OR_GREATER
namespace System;

/// <summary>
/// Polyfill for Guid methods not available in older .NET versions.
/// </summary>
internal static class GuidPolyfills
{
    /// <summary>
    /// Creates a new Guid according to RFC 9562, following the Version 7 format.
    /// This polyfill provides the functionality of <c>Guid.CreateVersion7()</c> for targets earlier than .NET 9.
    /// </summary>
    /// <returns>A new Guid with embedded timestamp for monotonic ordering.</returns>
    public static Guid CreateVersion7()
    {
        // UUID v7 format (RFC 9562):
        // - 48 bits: Unix timestamp in milliseconds (big-endian)
        // - 4 bits: version (0111 = 7)
        // - 12 bits: random
        // - 2 bits: variant (10)
        // - 62 bits: random

        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        byte[] bytes = new byte[16];

        // Fill with random data first
#if NETSTANDARD2_0
        using (var rng = Security.Cryptography.RandomNumberGenerator.Create())
        {
            rng.GetBytes(bytes);
        }
#else
        Security.Cryptography.RandomNumberGenerator.Fill(bytes);
#endif

        // Set timestamp (48 bits, big-endian) in first 6 bytes
        bytes[0] = (byte)(timestamp >> 40);
        bytes[1] = (byte)(timestamp >> 32);
        bytes[2] = (byte)(timestamp >> 24);
        bytes[3] = (byte)(timestamp >> 16);
        bytes[4] = (byte)(timestamp >> 8);
        bytes[5] = (byte)timestamp;

        // Set version 7 (0111) in high nibble of byte 6
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x70);

        // Set variant (10) in high 2 bits of byte 8
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);

        // Convert from big-endian byte array to Guid
        // Guid constructor expects bytes in a specific order for the first 8 bytes
        // (little-endian for the first three components on Windows)
        // We need to swap bytes to match the Guid's internal layout
        return new Guid(
            (int)(bytes[0] << 24 | bytes[1] << 16 | bytes[2] << 8 | bytes[3]),  // a (big-endian to int)
            (short)(bytes[4] << 8 | bytes[5]),  // b (big-endian to short)
            (short)(bytes[6] << 8 | bytes[7]),  // c (big-endian to short)
            bytes[8], bytes[9], bytes[10], bytes[11],
            bytes[12], bytes[13], bytes[14], bytes[15]);
    }
}
#endif
