using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using ModelContextProtocol.Client;

namespace ModelContextProtocol.Protocol;

/// <summary>
/// Represents the capability for a client to provide server-requested additional information during interactions.
/// </summary>
/// <remarks>
/// <para>
/// This capability enables the MCP client to respond to elicitation requests from an MCP server.
/// Clients must support at least one elicitation mode: form (in-band) or url (out-of-band via URL).
/// </para>
/// <para>
/// When this capability is enabled, an MCP server can request the client to provide additional information
/// during interactions. The client must set a <see cref="McpClientHandlers.ElicitationHandler"/> to process these requests.
/// </para>
/// <para>
/// Two modes of elicitation are supported:
/// <list type="bullet">
///   <item><description><b>form</b>: In-band elicitation where data is collected via a form and exposed to the client</description></item>
///   <item><description><b>url</b>: URL mode (out-of-band) elicitation via navigation where sensitive data is not exposed to the client</description></item>
/// </list>
/// </para>
/// </remarks>
[JsonConverter(typeof(Converter))]
public sealed class ElicitationCapability
{
    /// <summary>
    /// Gets or sets the form mode elicitation (in-band) capability, indicating support for in-band elicitation.
    /// </summary>
    /// <remarks>
    /// When present, indicates the client supports form mode elicitation where structured data
    /// is collected through a form interface and returned to the server.
    /// </remarks>
    [JsonPropertyName("form")]
    public FormElicitationCapability? Form { get; set; }

    /// <summary>
    /// Gets or sets the URL mode (out-of-band) elicitation capability.
    /// </summary>
    /// <remarks>
    /// When present, indicates the client supports URL mode elicitation for secure out-of-band
    /// interactions such as OAuth flows, payments, or collecting sensitive credentials.
    /// </remarks>
    [JsonPropertyName("url")]
    public UrlElicitationCapability? Url { get; set; }

    /// <summary>
    /// Gets or sets the handler for processing <see cref="RequestMethods.ElicitationCreate"/> requests.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This handler function is called when an MCP server requests the client to provide additional
    /// information during interactions. The client must set this property for the elicitation capability to work.
    /// </para>
    /// <para>
    /// The handler receives message parameters and a cancellation token.
    /// It should return a <see cref="ElicitResult"/> containing the response to the elicitation request.
    /// </para>
    /// </remarks>
    [JsonIgnore]
    [Obsolete($"Use {nameof(McpClientOptions.Handlers.ElicitationHandler)} instead. This member will be removed in a subsequent release.")] // See: https://github.com/modelcontextprotocol/csharp-sdk/issues/774
    [EditorBrowsable(EditorBrowsableState.Never)]
    public Func<ElicitRequestParams?, CancellationToken, ValueTask<ElicitResult>>? ElicitationHandler { get; set; }

    /// <summary>
    /// Provides a converter that normalizes blank capability objects to imply form support.
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public sealed class Converter : JsonConverter<ElicitationCapability>
    {
        /// <inheritdoc />
        public override ElicitationCapability? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Null)
            {
                return null;
            }

            using var document = JsonDocument.ParseValue(ref reader);

            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new JsonException("elicitation capability must be an object.");
            }

            var capability = new ElicitationCapability();
            bool hasForm = false;
            bool hasUrl = false;

            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (property.NameEquals("form"))
                {
                    capability.Form = property.Value.ValueKind == JsonValueKind.Null
                        ? null
                        : capability.Form ?? new FormElicitationCapability();
                    hasForm = true;
                }
                else if (property.NameEquals("url"))
                {
                    capability.Url = property.Value.ValueKind == JsonValueKind.Null
                        ? null
                        : capability.Url ?? new UrlElicitationCapability();
                    hasUrl = capability.Url is not null;
                }
            }

            if (!hasForm && !hasUrl)
            {
                capability.Form = new FormElicitationCapability();
            }

            return capability;
        }

        /// <inheritdoc />
        public override void Write(Utf8JsonWriter writer, ElicitationCapability value, JsonSerializerOptions options)
        {
            Throw.IfNull(writer);

            writer.WriteStartObject();

            bool writeForm = value.Form is not null || value.Url is null;
            if (writeForm)
            {
                writer.WritePropertyName("form");
                writer.WriteStartObject();
                writer.WriteEndObject();
            }

            if (value.Url is not null)
            {
                writer.WritePropertyName("url");
                writer.WriteStartObject();
                writer.WriteEndObject();
            }

            writer.WriteEndObject();
        }
    }
}
