namespace System;

/// <summary>
/// Provides polyfills for GUID generation methods not available in older .NET versions,
/// with monotonic counter-based ordering for strict intra-millisecond sequencing.
/// </summary>
internal static class GuidPolyfills
{
    private static long s_lastTimestamp;
    private static long s_counter;
    private static readonly object s_lock = new();

    /// <summary>
    /// Creates a UUID v7 GUID with the specified timestamp.
    /// Uses a counter for intra-millisecond ordering to ensure strict monotonicity.
    /// </summary>
    /// <param name="timestamp">The timestamp to embed in the GUID.</param>
    /// <returns>A new UUID v7 GUID.</returns>
    /// <remarks>
    /// Unlike the built-in <c>Guid.CreateVersion7(DateTimeOffset)</c> in .NET 9+,
    /// this implementation uses a counter to ensure strict monotonicity within the same millisecond,
    /// which is required for keyset pagination to work correctly.
    /// </remarks>
    public static Guid CreateVersion7(DateTimeOffset timestamp)
    {
        // UUID v7 format (RFC 9562):
        // - 48 bits: Unix timestamp in milliseconds (big-endian)
        // - 4 bits: version (0111 = 7)
        // - 12 bits: counter/sequence (for intra-millisecond ordering)
        // - 2 bits: variant (10)
        // - 62 bits: random

        long timestampMs = timestamp.ToUnixTimeMilliseconds();
        long counter;

        lock (s_lock)
        {
            if (timestampMs == s_lastTimestamp)
            {
                // Same millisecond - increment counter
                s_counter++;
            }
            else
            {
                // New millisecond - reset counter
                s_lastTimestamp = timestampMs;
                s_counter = 0;
            }

            counter = s_counter;
        }

        byte[] bytes = new byte[16];

        // Fill lower random bits (last 8 bytes) with random data
#if NETSTANDARD2_0
        using (var rng = Security.Cryptography.RandomNumberGenerator.Create())
        {
            rng.GetBytes(bytes, 8, 8);
        }
#else
        Security.Cryptography.RandomNumberGenerator.Fill(bytes.AsSpan(8, 8));
#endif

        // Set timestamp (48 bits, big-endian) in first 6 bytes
        bytes[0] = (byte)(timestampMs >> 40);
        bytes[1] = (byte)(timestampMs >> 32);
        bytes[2] = (byte)(timestampMs >> 24);
        bytes[3] = (byte)(timestampMs >> 16);
        bytes[4] = (byte)(timestampMs >> 8);
        bytes[5] = (byte)timestampMs;

        // Set version 7 (0111) in high nibble of byte 6, and high 4 bits of counter in low nibble
        bytes[6] = (byte)(0x70 | ((counter >> 8) & 0x0F));

        // Set remaining 8 bits of counter in byte 7
        bytes[7] = (byte)(counter & 0xFF);

        // Set variant (10) in high 2 bits of byte 8
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);

        // Convert from big-endian byte array to Guid
        return new Guid(
            (int)(bytes[0] << 24 | bytes[1] << 16 | bytes[2] << 8 | bytes[3]),
            (short)(bytes[4] << 8 | bytes[5]),
            (short)(bytes[6] << 8 | bytes[7]),
            bytes[8], bytes[9], bytes[10], bytes[11],
            bytes[12], bytes[13], bytes[14], bytes[15]);
    }
}

