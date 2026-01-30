using System.Threading;

namespace System;

/// <summary>
/// Provides helper methods for GUID generation.
/// </summary>
internal static class GuidHelpers
{
    private static long s_counter;

    /// <summary>
    /// Creates a monotonically increasing GUID using a UUIDv7-like format with the specified timestamp.
    /// Uses a globally increasing counter for strict monotonicity.
    /// </summary>
    /// <param name="timestamp">The timestamp to embed in the GUID.</param>
    /// <returns>A new monotonically increasing GUID.</returns>
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
    /// The format is UUIDv7-like but not RFC 9562 compliant since we prioritize strict
    /// monotonicity over random bits in the counter field.
    /// </para>
    /// </remarks>
    public static Guid CreateMonotonicUuid(DateTimeOffset timestamp)
    {
        // UUIDv7-like format (based on RFC 9562 structure, but uses counter instead of random for strict monotonicity):
        // - 48 bits: Unix timestamp in milliseconds (big-endian)
        // - 4 bits: version (0111 = 7)
        // - 12 bits: counter/sequence (for intra-millisecond ordering)
        // - 2 bits: variant (10)
        // - 62 bits: random

        long timestampMs = timestamp.ToUnixTimeMilliseconds();
        long counter = Interlocked.Increment(ref s_counter);

        // Start with a random GUID and twiddle the relevant bits
        Guid guid = Guid.NewGuid();

        unsafe
        {
            int* guidAsInts = (int*)&guid;
            short* guidAsShorts = (short*)&guid;
            byte* guidAsBytes = (byte*)&guid;

            // Set timestamp (48 bits) and version/counter using little-endian layout
            guidAsInts[0] = (int)(timestampMs >> 8);
            guidAsShorts[2] = (short)((timestampMs & 0xFF) | ((timestampMs >> 40) << 8));
            guidAsShorts[3] = (short)((counter & 0xFFF) | 0x7000);
            guidAsBytes[8] = (byte)((guidAsBytes[8] & 0x3F) | 0x80);
        }

        return guid;
    }
}
