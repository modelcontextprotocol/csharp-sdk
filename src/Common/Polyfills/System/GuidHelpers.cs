using System.Threading;

namespace System;

/// <summary>
/// Provides helper methods for GUID generation.
/// </summary>
internal static class GuidHelpers
{
    private static long s_counter;

    /// <summary>
    /// Creates a monotonically increasing UUID v7 GUID with the specified timestamp.
    /// Uses a globally increasing counter for strict monotonicity.
    /// </summary>
    /// <param name="timestamp">The timestamp to embed in the GUID.</param>
    /// <returns>A new monotonically increasing UUID v7 GUID.</returns>
    /// <remarks>
    /// <para>
    /// This method cannot be replaced with <c>Guid.CreateVersion7(DateTimeOffset)</c> because
    /// the built-in .NET implementation uses random bits for intra-millisecond uniqueness,
    /// which does not guarantee strict monotonicity. For keyset pagination to work correctly,
    /// GUIDs created within the same millisecond must be strictly ordered by creation time.
    /// </para>
    /// <para>
    /// This implementation uses a globally monotonically increasing counter to ensure that
    /// all generated GUIDs are strictly ordered by creation time, regardless of timestamp.
    /// </para>
    /// </remarks>
    public static Guid CreateMonotonicUuid(DateTimeOffset timestamp)
    {
        // UUID v7 format (RFC 9562):
        // - 48 bits: Unix timestamp in milliseconds (big-endian)
        // - 4 bits: version (0111 = 7)
        // - 12 bits: counter/sequence (for intra-millisecond ordering)
        // - 2 bits: variant (10)
        // - 62 bits: random

        long timestampMs = timestamp.ToUnixTimeMilliseconds();
        long counter = Interlocked.Increment(ref s_counter);

        // Start with a random GUID and twiddle the relevant bits
        Guid baseGuid = Guid.NewGuid();

#if NETSTANDARD2_0
        byte[] bytes = baseGuid.ToByteArray();
#else
        Span<byte> bytes = stackalloc byte[16];
        baseGuid.TryWriteBytes(bytes);
#endif

        // Guid.ToByteArray() returns bytes in little-endian order for the first 3 components,
        // but we need big-endian for UUID v7. The byte layout from ToByteArray() is:
        // [0-3]: Data1 (little-endian int)
        // [4-5]: Data2 (little-endian short)
        // [6-7]: Data3 (little-endian short)
        // [8-15]: Data4 (byte array, unchanged)

        // Set timestamp (48 bits) - need to account for little-endian layout
        // Data1 (bytes 0-3, little-endian) contains timestamp bits 0-31
        bytes[0] = (byte)(timestampMs >> 8);
        bytes[1] = (byte)(timestampMs >> 16);
        bytes[2] = (byte)(timestampMs >> 24);
        bytes[3] = (byte)(timestampMs >> 32);

        // Data2 (bytes 4-5, little-endian) contains timestamp bits 32-47
        bytes[4] = (byte)timestampMs;
        bytes[5] = (byte)(timestampMs >> 40);

        // Data3 (bytes 6-7, little-endian) contains version (4 bits) + counter high (12 bits)
        // Version 7 = 0111, counter uses 12 bits
        bytes[6] = (byte)(counter & 0xFF);
        bytes[7] = (byte)(0x70 | ((counter >> 8) & 0x0F));

        // Set variant (10) in high 2 bits of byte 8
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);

        return new Guid(bytes);
    }
}
