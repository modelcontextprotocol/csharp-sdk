using ModelContextProtocol.Utils;
using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ModelContextProtocol.Protocol.Messages;

/// <summary>
/// Represents a JSON-RPC request identifier, which can be either a string or an integer.
/// </summary>
[JsonConverter(typeof(Converter))]
public readonly struct RequestId : IEquatable<RequestId>
{
    /// <summary>The id, either a string or a boxed long or null.</summary>
    private readonly object? _id;

    /// <summary>Initializes a new instance of the <see cref="RequestId"/> with a specified value.</summary>
    /// <param name="value">The required ID value.</param>
    public RequestId(string value)
    {
        Throw.IfNull(value);
        _id = value;
    }

    /// <summary>Initializes a new instance of the <see cref="RequestId"/> with a specified value.</summary>
    /// <param name="value">The required ID value.</param>
    public RequestId(long value)
    {
        // Box the long. Request IDs are almost always strings in practice, so this should be rare.
        _id = value;
    }

    /// <summary>Gets whether the identifier is uninitialized.</summary>
    public bool IsDefault => _id is null;

    /// <inheritdoc />
    public override string ToString() =>
        _id is string stringValue ? $"\"{stringValue}\"" :
        _id is long longValue ? longValue.ToString(CultureInfo.InvariantCulture) :
        string.Empty;

    /// <summary>
    /// Compares this RequestId to another RequestId.
    /// </summary>
    public bool Equals(RequestId other) => Equals(_id, other._id);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is RequestId other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => _id?.GetHashCode() ?? 0;

    /// <summary>
    /// Compares two RequestIds for equality.
    /// </summary>
    public static bool operator ==(RequestId left, RequestId right) => left.Equals(right);

    /// <summary>
    /// Compares two RequestIds for inequality.
    /// </summary>
    public static bool operator !=(RequestId left, RequestId right) => !left.Equals(right);

    /// <summary>
    /// JSON converter for RequestId that handles both string and number values.
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public sealed class Converter: JsonConverter<RequestId>
    {
        /// <inheritdoc />
        public override RequestId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            return reader.TokenType switch
            {
                JsonTokenType.String => new(reader.GetString()!),
                JsonTokenType.Number => new(reader.GetInt64()),
                _ => throw new JsonException("requestId must be a string or an integer"),
            };
        }

        /// <inheritdoc />
        public override void Write(Utf8JsonWriter writer, RequestId value, JsonSerializerOptions options)
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
