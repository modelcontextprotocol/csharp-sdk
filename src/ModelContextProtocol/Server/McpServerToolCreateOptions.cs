using System.ComponentModel;

namespace ModelContextProtocol.Server;

/// <summary>
/// Provides options for controlling the creation of an <see cref="McpServerTool"/>.
/// </summary>
/// <remarks>
/// <para>
/// These options allow for customizing the behavior and metadata of tools created with
/// <see cref="McpServerTool.Create"/>. They provide control over naming, description,
/// tool properties, and dependency injection integration.
/// </para>
/// <para>
/// When creating tools programmatically rather than using attributes, these options
/// provide the same level of configuration flexibility.
/// </para>
/// <para>
/// Example usage:
/// <code>
/// // Create a tool with custom options
/// var toolOptions = new McpServerToolCreateOptions
/// {
///     Name = "getWeather",
///     Description = "Gets the current weather for a specified city",
///     Title = "Get Weather Information",
///     ReadOnly = true,
///     OpenWorld = true,
///     Services = serviceProvider // For dependency injection
/// };
/// 
/// // Create a tool with the options
/// var weatherTool = McpServerTool.Create(
///     (string city) => $"The weather in {city} is sunny with 72°F.",
///     toolOptions);
///     
/// // Add to server options
/// serverOptions.Capabilities.Tools.ToolCollection.Add(weatherTool);
/// </code>
/// </para>
/// </remarks>
public sealed class McpServerToolCreateOptions
{
    /// <summary>
    /// Gets or sets optional services used in the construction of the <see cref="McpServerTool"/>.
    /// </summary>
    /// <remarks>
    /// These services will be used to determine which parameters should be satisifed from dependency injection; what services
    /// are satisfied via this provider should match what's satisfied via the provider passed in at invocation time.
    /// </remarks>
    public IServiceProvider? Services { get; set; }

    /// <summary>
    /// Gets or sets the name to use for the <see cref="McpServerTool"/>.
    /// </summary>
    /// <remarks>
    /// If <see langword="null"/>, but an <see cref="McpServerToolAttribute"/> is applied to the method,
    /// the name from the attribute will be used. If that's not present, a name based on the method's name will be used.
    /// </remarks>
    public string? Name { get; set; }

    /// <summary>
    /// Gets or set the description to use for the <see cref="McpServerTool"/>.
    /// </summary>
    /// <remarks>
    /// If <see langword="null"/>, but a <see cref="DescriptionAttribute"/> is applied to the method,
    /// the description from that attribute will be used.
    /// </remarks>
    public string? Description { get; set; }

    /// <summary>
    /// Gets or sets a human-readable title for the tool that can be displayed to users.
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
    /// // Create a tool with a descriptive title
    /// var weatherTool = McpServerTool.Create(
    ///     (string city) => $"The weather in {city} is sunny.",
    ///     new McpServerToolCreateOptions { 
    ///         Name = "getWeather", 
    ///         Title = "Weather Information Service",
    ///         ReadOnly = true 
    ///     });
    /// </code>
    /// </example>
    public string? Title { get; set; }

    /// <summary>
    /// Gets or sets whether the tool may perform destructive updates to its environment.
    /// If true, the tool may perform destructive updates to its environment.
    /// If false, the tool performs only additive updates.
    /// This property is most relevant when the tool modifies its environment (ReadOnly = false).
    /// Default: true.
    /// </summary>
    public bool? Destructive { get; set; }

    /// <summary>
    /// Gets or sets whether calling the tool repeatedly with the same arguments 
    /// will have no additional effect on its environment.
    /// This property is most relevant when the tool modifies its environment (ReadOnly = false).
    /// Default: false.
    /// </summary>
    public bool? Idempotent { get; set; }

    /// <summary>
    /// Gets or sets whether this tool may interact with an "open world" of external entities.
    /// If true, the tool may interact with an unpredictable or dynamic set of entities (like web search).
    /// If false, the tool's domain of interaction is closed and well-defined (like memory access).
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
    public bool? OpenWorld { get; set; }

    /// <summary>
    /// Gets or sets whether this tool does not modify its environment.
    /// If true, the tool only performs read operations without changing state.
    /// If false, the tool may make modifications to its environment.
    /// Default: false.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Read-only tools are guaranteed not to have side effects beyond computational resource usage.
    /// They don't create, update, or delete data in any system.
    /// </para>
    /// <para>
    /// Setting this property helps clients understand the safety profile of the tool. Read-only
    /// tools can be called with minimal concerns about state changes or unintended consequences.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// // Create a read-only tool for getting information
    /// var weatherTool = McpServerTool.Create(
    ///     (string city) => $"The weather in {city} is sunny.",
    ///     new McpServerToolCreateOptions { 
    ///         Name = "getWeather", 
    ///         ReadOnly = true,
    ///         OpenWorld = true
    ///     });
    ///     
    /// // Create a tool that modifies data
    /// var updateTool = McpServerTool.Create(
    ///     (string id, string value) => _database.Update(id, value),
    ///     new McpServerToolCreateOptions { 
    ///         Name = "updateRecord", 
    ///         ReadOnly = false,
    ///         Destructive = true 
    ///     });
    /// </code>
    /// </example>
    public bool? ReadOnly { get; set; }

    /// <summary>
    /// Creates a shallow clone of the current <see cref="McpServerToolCreateOptions"/> instance.
    /// </summary>
    internal McpServerToolCreateOptions Clone() =>
        new McpServerToolCreateOptions()
        {
            Services = Services,
            Name = Name,
            Description = Description,
            Title = Title,
            Destructive = Destructive,
            Idempotent = Idempotent,
            OpenWorld = OpenWorld,
            ReadOnly = ReadOnly
        };
}
