using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ModelContextProtocol.Protocol;

/// <summary>
/// Represents a client's response to a server-initiated <see cref="InputRequest"/> as part of an MRTR
/// (Multi Round-Trip Request) flow.
/// </summary>
/// <remarks>
/// <para>
/// An <see cref="InputResponse"/> wraps the result of a server-to-client request such as
/// <see cref="CreateMessageResult"/>, <see cref="ElicitResult"/>, or <see cref="ListRootsResult"/>.
/// The type of the inner response corresponds to the <see cref="InputRequest.Method"/> of the
/// associated input request.
/// </para>
/// <para>
/// The input response does not carry its own type discriminator in JSON. The type is determined by
/// the corresponding <see cref="InputRequest.Method"/> key in the <see cref="IncompleteResult.InputRequests"/> map.
/// </para>
/// </remarks>
[Experimental(Experimentals.Mrtr_DiagnosticId, UrlFormat = Experimentals.Mrtr_Url)]
[JsonConverter(typeof(Converter))]
public sealed class InputResponse
{
    /// <summary>
    /// Gets or sets the raw JSON element representing the response.
    /// </summary>
    /// <remarks>
    /// Use <see cref="Deserialize{T}"/> or the typed factory methods to work with concrete response types.
    /// </remarks>
    [JsonIgnore]
    public JsonElement RawValue { get; set; }

    /// <summary>
    /// Deserializes the raw value to the specified result type.
    /// </summary>
    /// <typeparam name="T">The type to deserialize to (e.g., <see cref="CreateMessageResult"/>, <see cref="ElicitResult"/>).</typeparam>
    /// <param name="typeInfo">The JSON type information for <typeparamref name="T"/>.</param>
    /// <returns>The deserialized result, or <see langword="null"/> if deserialization fails.</returns>
    public T? Deserialize<T>(System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> typeInfo) =>
        JsonSerializer.Deserialize(RawValue, typeInfo);

    /// <summary>
    /// Gets the response as a <see cref="CreateMessageResult"/>.
    /// </summary>
    /// <returns>The deserialized sampling result, or <see langword="null"/> if deserialization fails.</returns>
    [JsonIgnore]
    public CreateMessageResult? SamplingResult =>
        JsonSerializer.Deserialize(RawValue, McpJsonUtilities.JsonContext.Default.CreateMessageResult);

    /// <summary>
    /// Gets the response as an <see cref="ElicitResult"/>.
    /// </summary>
    /// <returns>The deserialized elicitation result, or <see langword="null"/> if deserialization fails.</returns>
    [JsonIgnore]
    public ElicitResult? ElicitationResult =>
        JsonSerializer.Deserialize(RawValue, McpJsonUtilities.JsonContext.Default.ElicitResult);

    /// <summary>
    /// Gets the response as a <see cref="ListRootsResult"/>.
    /// </summary>
    /// <returns>The deserialized roots list result, or <see langword="null"/> if deserialization fails.</returns>
    [JsonIgnore]
    public ListRootsResult? RootsResult =>
        JsonSerializer.Deserialize(RawValue, McpJsonUtilities.JsonContext.Default.ListRootsResult);

    /// <summary>
    /// Creates an <see cref="InputResponse"/> from a <see cref="CreateMessageResult"/>.
    /// </summary>
    /// <param name="result">The sampling result.</param>
    /// <returns>A new <see cref="InputResponse"/> instance.</returns>
    public static InputResponse FromSamplingResult(CreateMessageResult result) => new()
    {
        RawValue = JsonSerializer.SerializeToElement(result, McpJsonUtilities.JsonContext.Default.CreateMessageResult),
    };

    /// <summary>
    /// Creates an <see cref="InputResponse"/> from an <see cref="ElicitResult"/>.
    /// </summary>
    /// <param name="result">The elicitation result.</param>
    /// <returns>A new <see cref="InputResponse"/> instance.</returns>
    public static InputResponse FromElicitResult(ElicitResult result) => new()
    {
        RawValue = JsonSerializer.SerializeToElement(result, McpJsonUtilities.JsonContext.Default.ElicitResult),
    };

    /// <summary>
    /// Creates an <see cref="InputResponse"/> from a <see cref="ListRootsResult"/>.
    /// </summary>
    /// <param name="result">The roots list result.</param>
    /// <returns>A new <see cref="InputResponse"/> instance.</returns>
    public static InputResponse FromRootsResult(ListRootsResult result) => new()
    {
        RawValue = JsonSerializer.SerializeToElement(result, McpJsonUtilities.JsonContext.Default.ListRootsResult),
    };

    /// <summary>Provides JSON serialization support for <see cref="InputResponse"/>.</summary>
    public sealed class Converter : JsonConverter<InputResponse>
    {
        /// <inheritdoc/>
        public override InputResponse? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var element = JsonElement.ParseValue(ref reader);
            return new InputResponse { RawValue = element };
        }

        /// <inheritdoc/>
        public override void Write(Utf8JsonWriter writer, InputResponse value, JsonSerializerOptions options)
        {
            value.RawValue.WriteTo(writer);
        }
    }
}
