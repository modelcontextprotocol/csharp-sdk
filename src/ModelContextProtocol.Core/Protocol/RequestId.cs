using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ModelContextProtocol.Protocol;

/// <summary>
/// Represents a JSON-RPC request identifier, which can be either a string or an integer.
/// </summary>
[JsonConverter(typeof(Converter))]
public readonly struct RequestId : IEquatable<RequestId>
{
    /// <summary>Initializes a new instance of the <see cref="RequestId"/> with a specified value.</summary>
    /// <param name="value">The required ID value.</param>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is <see langword="null"/>.</exception>
    public RequestId(string value)
    {
        Throw.IfNull(value);
        Id = value;
    }

    /// <summary>Initializes a new instance of the <see cref="RequestId"/> with a specified value.</summary>
    /// <param name="value">The required ID value.</param>
    public RequestId(long value)
    {
        // Box the long. Request IDs are almost always strings in practice, so this should be rare.
        Id = value;
    }

    /// <summary>Gets the underlying object for this ID.</summary>
    /// <remarks>This object will either be a <see cref="string"/>, a boxed <see cref="long"/>, or <see langword="null"/>.</remarks>
    public object? Id { get; }

    /// <inheritdoc />
    public override string ToString() =>
        Id is string stringValue ? stringValue :
        Id is long longValue ? longValue.ToString(CultureInfo.InvariantCulture) :
        string.Empty;

    /// <inheritdoc />
    public bool Equals(RequestId other) => Equals(Id, other.Id);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is RequestId other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => Id?.GetHashCode() ?? 0;

    /// <inheritdoc />
    public static bool operator ==(RequestId left, RequestId right) => left.Equals(right);

    /// <inheritdoc />
    public static bool operator !=(RequestId left, RequestId right) => !left.Equals(right);

    /// <summary>
    /// Provides a <see cref="JsonConverter"/> for <see cref="RequestId"/> that handles both string and number values.
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public sealed class Converter : JsonConverter<RequestId>
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

            switch (value.Id)
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
