using System.Text.Json.Serialization;

namespace ModelContextProtocol.Protocol.Types;

/// <summary>
/// Additional properties describing a Tool to clients.
/// NOTE: all properties in ToolAnnotations are **hints**.
/// They are not guaranteed to provide a faithful description of tool behavior (including descriptive properties like `title`).
/// Clients should never make tool use decisions based on ToolAnnotations received from untrusted servers.
/// <see href="https://github.com/modelcontextprotocol/specification/blob/main/schema/">See the schema for details</see>
/// There are multiple subtypes of content, depending on the "type" field, these are represented as separate classes.
/// </summary>
/// <example>
/// <code>
/// // Create a tool with annotations indicating it's read-only and operates in a closed world
/// var tool = new Tool
/// {
///     Name = "getWeather",
///     Description = "Gets the current weather for a location",
///     Annotations = new ToolAnnotations
///     {
///         Title = "Weather Information",
///         ReadOnlyHint = true,
///         OpenWorldHint = false
///     }
/// };
/// 
/// // Create a tool with annotations indicating it's destructive but idempotent
/// var updateTool = new Tool
/// {
///     Name = "updateRecord",
///     Description = "Updates a record in the database",
///     Annotations = new ToolAnnotations
///     {
///         Title = "Update Database Record",
///         DestructiveHint = true,
///         IdempotentHint = true
///     }
/// };
/// </code>
/// </example>
public class ToolAnnotations
{
    /// <summary>
    /// A human-readable title for the tool that can be displayed to users.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The title provides a more descriptive, user-friendly name for the tool than the tool's
    /// programmatic name. It is intended for display purposes and to help users understand
    /// the tool's purpose at a glance.
    /// </para>
    /// <para>
    /// Unlike the tool name (which follows programmatic naming conventions), the title can
    /// include spaces, special characters, and be phrased in a more natural language style.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// var weatherTool = new Tool
    /// {
    ///     Name = "getWeather",  // Programmatic name
    ///     Description = "Gets the current weather for a location",
    ///     Annotations = new ToolAnnotations
    ///     {
    ///         Title = "Weather Information",  // User-friendly title
    ///         ReadOnlyHint = true
    ///     }
    /// };
    /// </code>
    /// </example>
    [JsonPropertyName("title")]
    public string? Title { get; set; }

    /// <summary>
    /// If true, the tool may perform destructive updates to its environment.
    /// If false, the tool performs only additive updates.
    /// (This property is meaningful only when <see cref="ReadOnlyHint"/> is false).
    /// Default: true.
    /// </summary>
    [JsonPropertyName("destructiveHint")]
    public bool? DestructiveHint { get; set; }

    /// <summary>
    /// If true, calling the tool repeatedly with the same arguments 
    /// will have no additional effect on its environment.
    /// (This property is meaningful only when <see cref="ReadOnlyHint"/> is false).
    /// Default: false.
    /// </summary>
    [JsonPropertyName("idempotentHint")]
    public bool? IdempotentHint { get; set; }

    /// <summary>
    /// If true, this tool may interact with an "open world" of external entities.
    /// If false, the tool's domain of interaction is closed.
    /// For example, the world of a web search tool is open, whereas that of a memory tool is not.
    /// Default: true.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Open world tools typically interact with external systems or data that may change independently
    /// of the tool's operation, such as web searches, weather data, or stock prices.
    /// </para>
    /// <para>
    /// Closed world tools operate in a more controlled environment where the set of possible
    /// interactions is well-defined and predictable, such as memory management, mathematical calculations,
    /// or manipulating data structures that are fully contained within the system.
    /// </para>
    /// </remarks>
    [JsonPropertyName("openWorldHint")]
    public bool? OpenWorldHint { get; set; }

    /// <summary>
    /// If true, the tool does not modify its environment and only performs read operations.
    /// If false, the tool may modify its environment through its operations.
    /// Default: false.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Read-only tools are typically used for querying information, performing calculations,
    /// or retrieving data without making any persistent changes to the system or external resources.
    /// </para>
    /// <para>
    /// Examples of read-only tools include weather information retrieval, search functions,
    /// or mathematical calculations. Tools that create, update, or delete data would not be read-only.
    /// </para>
    /// <para>
    /// When this property is set to true, both <see cref="DestructiveHint"/> and <see cref="IdempotentHint"/> 
    /// properties are not applicable, as they only describe the behavior of tools that can modify their environment.
    /// </para>
    /// <para>
    /// Setting this hint appropriately helps AI models understand which tools are safe to use 
    /// for information gathering without risk of making changes to system state.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// // Create a read-only tool for retrieving information
    /// var infoTool = new Tool
    /// {
    ///     Name = "getSearchResults",
    ///     Description = "Searches for information on a topic",
    ///     Annotations = new ToolAnnotations
    ///     {
    ///         ReadOnlyHint = true
    ///     }
    /// };
    /// 
    /// // Configure a tool as not read-only (can modify data)
    /// var actionTool = new Tool
    /// {
    ///     Name = "saveDocument",
    ///     Description = "Saves changes to a document",
    ///     Annotations = new ToolAnnotations
    ///     {
    ///         ReadOnlyHint = false,
    ///         DestructiveHint = true
    ///     }
    /// };
    /// </code>
    /// </example>
    [JsonPropertyName("readOnlyHint")]
    public bool? ReadOnlyHint { get; set; }
}