using System.Threading;

namespace System;

/// <summary>
/// Provides helper methods for monotonic ID generation.
/// </summary>
internal static class IdHelpers
{
    private static long s_counter;

    /// <summary>
    /// Creates a strictly monotonically increasing identifier string using 64-bit timestamp ticks
    /// and a 64-bit counter, formatted as a 32-character hexadecimal string (GUID-like).
    /// </summary>
    /// <param name="timestamp">The timestamp to embed in the identifier.</param>
    /// <returns>A new strictly monotonically increasing identifier string.</returns>
    /// <remarks>
    /// <para>
    /// This method creates a 128-bit identifier composed of two 64-bit values:
    /// - High 64 bits: <see cref="DateTimeOffset.Ticks"/> from the timestamp
    /// - Low 64 bits: A globally monotonically increasing counter
    /// </para>
    /// <para>
    /// The resulting string is strictly monotonically increasing when compared lexicographically,
    /// which is required for keyset pagination to work correctly. Unlike <c>Guid.CreateVersion7</c>,
    /// which uses random bits for intra-millisecond uniqueness, this implementation guarantees
    /// strict ordering for all identifiers regardless of when they were created.
    /// </para>
    /// </remarks>
    public static string CreateMonotonicId(DateTimeOffset timestamp)
    {
        long ticks = timestamp.UtcTicks;
        long counter = Interlocked.Increment(ref s_counter);

        // Format as 32-character hex string (16 bytes = 128 bits)
        // High 64 bits: timestamp ticks, Low 64 bits: counter
        return $"{ticks:x16}{counter:x16}";
    }
}
