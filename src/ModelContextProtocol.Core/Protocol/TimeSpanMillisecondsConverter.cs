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
/// handles nullable TimeSpan properties using this converter.
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
                return TimeSpan.FromMilliseconds(milliseconds);
            }

            // For non-integer values, convert from fractional milliseconds
            double fractionalMilliseconds = reader.GetDouble();
            return TimeSpan.FromTicks((long)(fractionalMilliseconds * TimeSpan.TicksPerMillisecond));
        }

        throw new JsonException($"Unable to convert {reader.TokenType} to TimeSpan.");
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, TimeSpan value, JsonSerializerOptions options)
    {
        writer.WriteNumberValue((long)value.TotalMilliseconds);
    }
}
