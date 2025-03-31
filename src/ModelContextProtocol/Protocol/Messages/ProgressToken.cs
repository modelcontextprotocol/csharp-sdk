using ModelContextProtocol.Utils;
using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ModelContextProtocol.Protocol.Messages;

/// <summary>
/// Represents a progress token, which can be either a string or an integer.
/// </summary>
[JsonConverter(typeof(Converter))]
public readonly struct ProgressToken : IEquatable<ProgressToken>
{
    /// <summary>The id, either a string or a boxed long or null.</summary>
    private readonly object? _id;

    /// <summary>Initializes a new instance of the <see cref="ProgressToken"/> with a specified value.</summary>
    /// <param name="value">The required ID value.</param>
    public ProgressToken(string value)
    {
        Throw.IfNull(value);
        _id = value;
    }

    /// <summary>Initializes a new instance of the <see cref="ProgressToken"/> with a specified value.</summary>
    /// <param name="value">The required ID value.</param>
    public ProgressToken(long value)
    {
        // Box the long. Progress tokens are almost always strings in practice, so this should be rare.
        _id = value;
    }

    /// <summary>Gets whether the identifier is uninitialized.</summary>
    public bool IsDefault => _id is null;

    /// <inheritdoc />
    public override string? ToString() =>
        _id is string stringValue ? $"\"{stringValue}\"" :
        _id is long longValue ? longValue.ToString(CultureInfo.InvariantCulture) :
        null;

    /// <summary>
    /// Compares this ProgressToken to another ProgressToken.
    /// </summary>
    public bool Equals(ProgressToken other) => Equals(_id, other._id);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is ProgressToken other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => _id?.GetHashCode() ?? 0;

    /// <summary>
    /// Compares two ProgressTokens for equality.
    /// </summary>
    public static bool operator ==(ProgressToken left, ProgressToken right) => left.Equals(right);

    /// <summary>
    /// Compares two ProgressTokens for inequality.
    /// </summary>
    public static bool operator !=(ProgressToken left, ProgressToken right) => !left.Equals(right);

    /// <summary>
    /// JSON converter for ProgressToken that handles both string and number values.
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public sealed class Converter : JsonConverter<ProgressToken>
    {
        /// <inheritdoc />
        public override ProgressToken Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            return reader.TokenType switch
            {
                JsonTokenType.String => new(reader.GetString()!),
                JsonTokenType.Number => new(reader.GetInt64()),
                _ => throw new JsonException("progressToken must be a string or an integer"),
            };
        }

        /// <inheritdoc />
        public override void Write(Utf8JsonWriter writer, ProgressToken value, JsonSerializerOptions options)
        {
            Throw.IfNull(writer);

            switch (value._id)
            {
                case string str:
                    writer.WriteStringValue(str);
                    return;

                case long longValue:
                    writer.WriteNumberValue(longValue);
                    return;

                case null:
                    writer.WriteStringValue(string.Empty);
                    return;
            }
        }
    }
}
