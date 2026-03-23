using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ModelContextProtocol.Protocol;

/// <summary>
/// Represents a server-initiated request that the client must fulfill as part of an MRTR
/// (Multi Round-Trip Request) flow.
/// </summary>
/// <remarks>
/// <para>
/// An <see cref="InputRequest"/> wraps a server-to-client request such as
/// <see cref="RequestMethods.SamplingCreateMessage"/>, <see cref="RequestMethods.ElicitationCreate"/>,
/// or <see cref="RequestMethods.RootsList"/>. It is included in an <see cref="IncompleteResult"/>
/// when the server needs additional input before it can complete a client-initiated request.
/// </para>
/// <para>
/// The <see cref="Method"/> property identifies the type of request, and the corresponding
/// parameters can be accessed via the typed accessor properties.
/// </para>
/// </remarks>
[Experimental(Experimentals.Mrtr_DiagnosticId, UrlFormat = Experimentals.Mrtr_Url)]
[JsonConverter(typeof(Converter))]
public sealed class InputRequest
{
    /// <summary>
    /// Gets or sets the method name identifying the type of this input request.
    /// </summary>
    /// <remarks>
    /// Standard values include:
    /// <list type="bullet">
    ///   <item><term><see cref="RequestMethods.SamplingCreateMessage"/></term><description>A sampling request.</description></item>
    ///   <item><term><see cref="RequestMethods.ElicitationCreate"/></term><description>An elicitation request.</description></item>
    ///   <item><term><see cref="RequestMethods.RootsList"/></term><description>A roots list request.</description></item>
    /// </list>
    /// </remarks>
    [JsonPropertyName("method")]
    public required string Method { get; set; }

    /// <summary>
    /// Gets or sets the raw JSON parameters for this input request.
    /// </summary>
    /// <remarks>
    /// Use the typed accessor properties (<see cref="SamplingParams"/>, <see cref="ElicitationParams"/>,
    /// <see cref="RootsParams"/>) for convenient strongly-typed access.
    /// </remarks>
    [JsonPropertyName("params")]
    public JsonElement? Params { get; set; }

    /// <summary>
    /// Gets the parameters as <see cref="CreateMessageRequestParams"/> when <see cref="Method"/>
    /// is <see cref="RequestMethods.SamplingCreateMessage"/>.
    /// </summary>
    /// <returns>The deserialized sampling parameters, or <see langword="null"/> if the method does not match or params are absent.</returns>
    [JsonIgnore]
    public CreateMessageRequestParams? SamplingParams =>
        string.Equals(Method, RequestMethods.SamplingCreateMessage, StringComparison.Ordinal) && Params is { } p
            ? JsonSerializer.Deserialize(p, McpJsonUtilities.JsonContext.Default.CreateMessageRequestParams)
            : null;

    /// <summary>
    /// Gets the parameters as <see cref="ElicitRequestParams"/> when <see cref="Method"/>
    /// is <see cref="RequestMethods.ElicitationCreate"/>.
    /// </summary>
    /// <returns>The deserialized elicitation parameters, or <see langword="null"/> if the method does not match or params are absent.</returns>
    [JsonIgnore]
    public ElicitRequestParams? ElicitationParams =>
        string.Equals(Method, RequestMethods.ElicitationCreate, StringComparison.Ordinal) && Params is { } p
            ? JsonSerializer.Deserialize(p, McpJsonUtilities.JsonContext.Default.ElicitRequestParams)
            : null;

    /// <summary>
    /// Gets the parameters as <see cref="ListRootsRequestParams"/> when <see cref="Method"/>
    /// is <see cref="RequestMethods.RootsList"/>.
    /// </summary>
    /// <returns>The deserialized roots list parameters, or <see langword="null"/> if the method does not match or params are absent.</returns>
    [JsonIgnore]
    public ListRootsRequestParams? RootsParams =>
        string.Equals(Method, RequestMethods.RootsList, StringComparison.Ordinal) && Params is { } p
            ? JsonSerializer.Deserialize(p, McpJsonUtilities.JsonContext.Default.ListRootsRequestParams)
            : null;

    /// <summary>
    /// Creates an <see cref="InputRequest"/> for a sampling request.
    /// </summary>
    /// <param name="requestParams">The sampling request parameters.</param>
    /// <returns>A new <see cref="InputRequest"/> instance.</returns>
    public static InputRequest ForSampling(CreateMessageRequestParams requestParams)
    {
        Throw.IfNull(requestParams);
        return new()
        {
            Method = RequestMethods.SamplingCreateMessage,
            Params = JsonSerializer.SerializeToElement(requestParams, McpJsonUtilities.JsonContext.Default.CreateMessageRequestParams),
        };
    }

    /// <summary>
    /// Creates an <see cref="InputRequest"/> for an elicitation request.
    /// </summary>
    /// <param name="requestParams">The elicitation request parameters.</param>
    /// <returns>A new <see cref="InputRequest"/> instance.</returns>
    public static InputRequest ForElicitation(ElicitRequestParams requestParams)
    {
        Throw.IfNull(requestParams);
        return new()
        {
            Method = RequestMethods.ElicitationCreate,
            Params = JsonSerializer.SerializeToElement(requestParams, McpJsonUtilities.JsonContext.Default.ElicitRequestParams),
        };
    }

    /// <summary>
    /// Creates an <see cref="InputRequest"/> for a roots list request.
    /// </summary>
    /// <param name="requestParams">The roots list request parameters.</param>
    /// <returns>A new <see cref="InputRequest"/> instance.</returns>
    public static InputRequest ForRootsList(ListRootsRequestParams requestParams)
    {
        Throw.IfNull(requestParams);
        return new()
        {
            Method = RequestMethods.RootsList,
            Params = JsonSerializer.SerializeToElement(requestParams, McpJsonUtilities.JsonContext.Default.ListRootsRequestParams),
        };
    }

    /// <summary>Provides JSON serialization support for <see cref="InputRequest"/>.</summary>
    public sealed class Converter : JsonConverter<InputRequest>
    {
        /// <inheritdoc/>
        public override InputRequest? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.StartObject)
            {
                throw new JsonException("Expected StartObject token.");
            }

            string? method = null;
            JsonElement? parameters = null;

            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndObject)
                {
                    break;
                }

                if (reader.TokenType != JsonTokenType.PropertyName)
                {
                    throw new JsonException("Expected PropertyName token.");
                }

                string propertyName = reader.GetString()!;
                reader.Read();

                switch (propertyName)
                {
                    case "method":
                        method = reader.GetString();
                        break;
                    case "params":
                        parameters = JsonElement.ParseValue(ref reader);
                        break;
                    default:
                        reader.Skip();
                        break;
                }
            }

            if (method is null)
            {
                throw new JsonException("InputRequest must have a 'method' property.");
            }

            return new InputRequest
            {
                Method = method,
                Params = parameters,
            };
        }

        /// <inheritdoc/>
        public override void Write(Utf8JsonWriter writer, InputRequest value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WriteString("method", value.Method);
            if (value.Params is { } p)
            {
                writer.WritePropertyName("params");
                p.WriteTo(writer);
            }
            writer.WriteEndObject();
        }
    }
}
