using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

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
/// the corresponding <see cref="InputRequest.Method"/> key in the <see cref="InputRequiredResult.InputRequests"/> map.
/// </para>
/// </remarks>
[JsonConverter(typeof(Converter))]
public sealed class InputResponse
{
    /// <summary>
    /// Gets or sets the raw JSON element representing the response.
    /// </summary>
    /// <remarks>
    /// Use <see cref="Deserialize{T}"/> with the <c>JsonTypeInfo&lt;T&gt;</c> matching the
    /// associated <see cref="InputRequest.Method"/> - for elicitation, sampling, or roots see
    /// <see cref="ElicitResultJsonTypeInfo"/>, <see cref="CreateMessageResultJsonTypeInfo"/>, and
    /// <see cref="ListRootsResultJsonTypeInfo"/>.
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
    /// Gets the <see cref="JsonTypeInfo{T}"/> for <see cref="ElicitResult"/>, suitable for use with
    /// <see cref="Deserialize{T}"/> when the corresponding <see cref="InputRequest.Method"/> is
    /// <see cref="RequestMethods.ElicitationCreate"/>.
    /// </summary>
    public static JsonTypeInfo<ElicitResult> ElicitResultJsonTypeInfo => McpJsonUtilities.JsonContext.Default.ElicitResult;

    /// <summary>
    /// Gets the <see cref="JsonTypeInfo{T}"/> for <see cref="CreateMessageResult"/>, suitable for use with
    /// <see cref="Deserialize{T}"/> when the corresponding <see cref="InputRequest.Method"/> is
    /// <see cref="RequestMethods.SamplingCreateMessage"/>.
    /// </summary>
    [Obsolete(Obsoletions.DeprecatedSampling_Message, DiagnosticId = Obsoletions.Deprecated_DiagnosticId, UrlFormat = Obsoletions.Deprecated_Url)]
    public static JsonTypeInfo<CreateMessageResult> CreateMessageResultJsonTypeInfo => McpJsonUtilities.JsonContext.Default.CreateMessageResult;

    /// <summary>
    /// Gets the <see cref="JsonTypeInfo{T}"/> for <see cref="ListRootsResult"/>, suitable for use with
    /// <see cref="Deserialize{T}"/> when the corresponding <see cref="InputRequest.Method"/> is
    /// <see cref="RequestMethods.RootsList"/>.
    /// </summary>
    [Obsolete(Obsoletions.DeprecatedRoots_Message, DiagnosticId = Obsoletions.Deprecated_DiagnosticId, UrlFormat = Obsoletions.Deprecated_Url)]
    public static JsonTypeInfo<ListRootsResult> ListRootsResultJsonTypeInfo => McpJsonUtilities.JsonContext.Default.ListRootsResult;

    /// <summary>
    /// Creates an <see cref="InputResponse"/> from a <see cref="CreateMessageResult"/>.
    /// </summary>
    /// <param name="result">The sampling result.</param>
    /// <returns>A new <see cref="InputResponse"/> instance.</returns>
    [Obsolete(Obsoletions.DeprecatedSampling_Message, DiagnosticId = Obsoletions.Deprecated_DiagnosticId, UrlFormat = Obsoletions.Deprecated_Url)]
    public static InputResponse FromSamplingResult(CreateMessageResult result)
    {
        Throw.IfNull(result);
        return new()
        {
            RawValue = JsonSerializer.SerializeToElement(result, McpJsonUtilities.JsonContext.Default.CreateMessageResult),
        };
    }

    /// <summary>
    /// Creates an <see cref="InputResponse"/> from an <see cref="ElicitResult"/>.
    /// </summary>
    /// <param name="result">The elicitation result.</param>
    /// <returns>A new <see cref="InputResponse"/> instance.</returns>
    public static InputResponse FromElicitResult(ElicitResult result)
    {
        Throw.IfNull(result);
        return new()
        {
            RawValue = JsonSerializer.SerializeToElement(result, McpJsonUtilities.JsonContext.Default.ElicitResult),
        };
    }

    /// <summary>
    /// Creates an <see cref="InputResponse"/> from a <see cref="ListRootsResult"/>.
    /// </summary>
    /// <param name="result">The roots list result.</param>
    /// <returns>A new <see cref="InputResponse"/> instance.</returns>
    [Obsolete(Obsoletions.DeprecatedRoots_Message, DiagnosticId = Obsoletions.Deprecated_DiagnosticId, UrlFormat = Obsoletions.Deprecated_Url)]
    public static InputResponse FromRootsResult(ListRootsResult result)
    {
        Throw.IfNull(result);
        return new()
        {
            RawValue = JsonSerializer.SerializeToElement(result, McpJsonUtilities.JsonContext.Default.ListRootsResult),
        };
    }

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
