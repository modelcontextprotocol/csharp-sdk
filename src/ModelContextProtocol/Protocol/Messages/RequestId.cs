using ModelContextProtocol.Utils;
using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;

namespace ModelContextProtocol.Protocol.Messages;

/// <summary>
/// Represents a JSON-RPC request identifier, which can be either a string or an integer.
/// </summary>
/// <remarks>
/// <para>
/// In JSON-RPC, request IDs are used to correlate requests with their corresponding responses.
/// The MCP implementation supports both string and integer IDs to comply with the JSON-RPC specification.
/// </para>
/// <para>
/// Usage examples:
/// <code>
/// // Create a string-based request ID
/// RequestId stringId = new RequestId("request-123");
/// 
/// // Create a numeric request ID
/// RequestId numericId = new RequestId(42);
/// 
/// // Generate a unique request ID
/// RequestId uniqueId = RequestId.Generate();
/// </code>
/// </para>
/// </remarks>
[JsonConverter(typeof(Converter))]
public readonly struct RequestId : IEquatable<RequestId>
{
    /// <summary>Sequential counter for generating unique request IDs.</summary>
    private static long s_nextId;
    
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
    
    /// <summary>
    /// Generates a unique request ID as a string.
    /// </summary>
    /// <returns>A new <see cref="RequestId"/> instance with a unique string value.</returns>
    /// <remarks>
    /// <para>
    /// The generated ID is a combination of a GUID and a sequential counter, making it suitable for tracing requests
    /// across components and ensuring uniqueness within a session.
    /// </para>
    /// <para>
    /// This method is typically used when creating new JSON-RPC requests that require a unique identifier:
    /// <code>
    /// var request = new JsonRpcRequest
    /// {
    ///     Method = "myMethod",
    ///     Id = RequestId.Generate(),
    ///     Params = new { param1 = "value" }
    /// };
    /// </code>
    /// </para>
    /// </remarks>
    public static RequestId Generate() => new(Guid.NewGuid().ToString("N") + "-" + Interlocked.Increment(ref s_nextId));

    /// <summary>Gets the underlying object for this id.</summary>
    /// <remarks>This will either be a <see cref="string"/>, a boxed <see cref="long"/>, or <see langword="null"/>.</remarks>
    public object? Id => _id;

    /// <inheritdoc />
    public override string ToString() =>
        _id is string stringValue ? stringValue :
        _id is long longValue ? longValue.ToString(CultureInfo.InvariantCulture) :
        string.Empty;

    /// <summary>
    /// Determines whether the specified <see cref="RequestId"/> is equal to the current <see cref="RequestId"/>.
    /// </summary>
    /// <param name="other">The <see cref="RequestId"/> to compare with the current object.</param>
    /// <returns><see langword="true"/> if the specified object is equal to the current object; otherwise, <see langword="false"/>.</returns>
    /// <remarks>
    /// The equality comparison is based on the underlying ID value, which can be a string, a long, or null.
    /// </remarks>
    public bool Equals(RequestId other) => Equals(_id, other._id);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is RequestId other && Equals(other);

    /// <summary>
    /// Returns a hash code for the current <see cref="RequestId"/> instance.
    /// </summary>
    /// <returns>
    /// A hash code for the current <see cref="RequestId"/> instance, calculated from the underlying ID value.
    /// Returns the hash code of the ID if it's not null, or 0 if the ID is null.
    /// </returns>
    /// <remarks>
    /// This implementation ensures that request IDs with the same underlying value will produce the same hash code,
    /// which is consistent with the equality comparison behavior defined in the <see cref="Equals(RequestId)"/> method.
    /// </remarks>
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
