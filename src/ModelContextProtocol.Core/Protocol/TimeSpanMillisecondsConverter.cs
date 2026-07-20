using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ModelContextProtocol.Protocol;

/// <summary>
/// Provides a JSON converter for <see cref="TimeSpan"/> that serializes as integer milliseconds.
/// </summary>
/// <remarks>
/// This converter serializes TimeSpan values as the total number of milliseconds (as an integer),
/// and deserializes integer millisecond values back to TimeSpan. System.Text.Json automatically
/// handles nullable TimeSpan properties using this converter. Millisecond values that fall outside
/// the range representable by <see cref="TimeSpan"/> are clamped to
/// <see cref="TimeSpan.MinValue"/>/<see cref="TimeSpan.MaxValue"/> rather than throwing, so an
/// oversized or malformed hint can never break deserialization of the enclosing result.
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class TimeSpanMillisecondsConverter : JsonConverter<TimeSpan>
{
    /// <inheritdoc />
    public override TimeSpan Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType is JsonTokenType.Number)
        {
            if (reader.TryGetInt64(out long milliseconds))
            {
                return FromMillisecondsClamped(milliseconds);
            }

            // Non-integer value: fractional, or a magnitude too large to represent. Use the non-throwing
            // TryGetDouble so an out-of-range exponent never breaks deserialization. Note that different
            // runtimes disagree on out-of-range doubles: in-box .NET returns +/-Infinity, whereas .NET
            // Framework's parser reports failure. Handle both so behavior is identical everywhere.
            if (reader.TryGetDouble(out double value))
            {
                if (double.IsPositiveInfinity(value))
                {
                    return TimeSpan.MaxValue;
                }

                if (double.IsNegativeInfinity(value))
                {
                    return TimeSpan.MinValue;
                }

                return FromTicksClamped(value * TimeSpan.TicksPerMillisecond);
            }

            // The runtime could not represent the number as a double at all (e.g. .NET Framework on an
            // overflowing exponent). Clamp by the sign of the raw token.
            return IsNegativeNumberToken(ref reader) ? TimeSpan.MinValue : TimeSpan.MaxValue;
        }

        throw new JsonException($"Unable to convert {reader.TokenType} to TimeSpan.");
    }

    private static bool IsNegativeNumberToken(ref Utf8JsonReader reader)
    {
        ReadOnlySpan<byte> token = reader.HasValueSequence ? reader.ValueSequence.First.Span : reader.ValueSpan;
        return !token.IsEmpty && token[0] == (byte)'-';
    }

    // Largest whole-millisecond count representable as a TimeSpan (TimeSpan.MaxValue.Ticks / TicksPerMillisecond).
    private const long MaxWholeMilliseconds = long.MaxValue / TimeSpan.TicksPerMillisecond;

    // Converts an integer millisecond count to a TimeSpan, clamping out-of-range values to
    // TimeSpan.MinValue/MaxValue instead of throwing. A malformed or oversized hint (for example a
    // hostile or buggy server returning an enormous ttlMs) must never break deserialization of the
    // whole result; per SEP-2549 clients should handle unexpected TTL values gracefully.
    private static TimeSpan FromMillisecondsClamped(long milliseconds)
    {
        if (milliseconds > MaxWholeMilliseconds)
        {
            return TimeSpan.MaxValue;
        }

        if (milliseconds < -MaxWholeMilliseconds)
        {
            return TimeSpan.MinValue;
        }

        return TimeSpan.FromTicks(milliseconds * TimeSpan.TicksPerMillisecond);
    }

    // Converts a (possibly fractional or out-of-range) tick count to a TimeSpan, clamping instead of
    // throwing. The caller passes a value already scaled into tick-space (milliseconds * TicksPerMillisecond)
    // because TimeSpan is backed by a long tick count, so comparing against long.MaxValue/MinValue is the
    // exact test for whether the final (long) cast would overflow. The comparisons MUST run before that cast:
    // double arithmetic saturates to +/-Infinity on overflow rather than throwing, and both infinities fall
    // into the clamp branches here (+Infinity >= long.MaxValue, -Infinity <= long.MinValue); if Infinity
    // instead reached "(long)ticks" the unchecked conversion would silently yield long.MinValue. NaN is not
    // reachable from valid JSON (the only multiplicand is a non-zero constant) but is mapped to zero
    // defensively so a non-numeric hint can never break deserialization.
    private static TimeSpan FromTicksClamped(double ticks)
    {
        if (double.IsNaN(ticks))
        {
            return TimeSpan.Zero;
        }

        if (ticks >= long.MaxValue)
        {
            return TimeSpan.MaxValue;
        }

        if (ticks <= long.MinValue)
        {
            return TimeSpan.MinValue;
        }

        return TimeSpan.FromTicks((long)ticks);
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, TimeSpan value, JsonSerializerOptions options)
    {
        writer.WriteNumberValue((long)value.TotalMilliseconds);
    }
}
