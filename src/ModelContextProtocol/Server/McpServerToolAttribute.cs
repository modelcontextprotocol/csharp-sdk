namespace ModelContextProtocol.Server;

/// <summary>
/// Used to indicate that a method should be considered an MCP tool and describe it.
/// </summary>
/// <remarks>
/// <para>
/// This attribute is applied to methods that should be exposed as tools in the Model Context Protocol. When a class 
/// containing methods marked with this attribute is registered with <see cref="McpServerBuilderExtensions.WithTools{TToolType}"/>,
/// these methods become available as tools that can be called by MCP clients.
/// </para>
/// <para>
/// Example usage:
/// <code>
/// [McpServerToolType]
/// public class CalculatorTools
/// {
///     [McpServerTool(Name = "add", ReadOnly = true)]
///     [Description("Adds two numbers together.")]
///     public static int Add(int a, int b)
///     {
///         return a + b;
///     }
/// }
/// </code>
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Method)]
public sealed class McpServerToolAttribute : Attribute
{
    // Defaults based on the spec
    private const bool DestructiveDefault = true;
    private const bool IdempotentDefault = false;
    private const bool OpenWorldDefault = true;
    private const bool ReadOnlyDefault = false;

    // Nullable backing fields so we can distinguish
    internal bool? _destructive;
    internal bool? _idempotent;
    internal bool? _openWorld;
    internal bool? _readOnly;

    /// <summary>
    /// Initializes a new instance of the <see cref="McpServerToolAttribute"/> class.
    /// </summary>
    public McpServerToolAttribute()
    {
    }

    /// <summary>Gets the name of the tool.</summary>
    /// <remarks>If <see langword="null"/>, the method name will be used.</remarks>
    public string? Name { get; set; }

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
    /// [McpServerTool(Name = "getWeather", Title = "Weather Information", ReadOnly = true)]
    /// public string GetWeather(string location)
    /// {
    ///     // Implementation...
    ///     return $"The weather in {location} is sunny.";
    /// }
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
    public bool Destructive 
    {
        get => _destructive ?? DestructiveDefault; 
        set => _destructive = value; 
    }

    /// <summary>
    /// Gets or sets whether calling the tool repeatedly with the same arguments will have no additional effect on its environment.
    /// This property is most relevant when the tool modifies its environment (ReadOnly = false).
    /// Default: false.
    /// </summary>
    public bool Idempotent  
    {
        get => _idempotent ?? IdempotentDefault;
        set => _idempotent = value; 
    }

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
    public bool OpenWorld
    {
        get => _openWorld ?? OpenWorldDefault; 
        set => _openWorld = value; 
    }

    /// <summary>
    /// Gets or sets whether the tool does not modify its environment.
    /// If true, the tool only performs read operations without changing state.
    /// If false, the tool may make modifications to its environment.
    /// Default: false.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Read-only tools should not create, update, or delete data, and should have no side effects
    /// beyond computational resource usage (CPU, memory, etc.).
    /// </para>
    /// <para>
    /// Examples of read-only tools include calculator functions, data retrieval operations,
    /// and search functionality. Setting this property appropriately helps clients understand
    /// the potential impact of calling the tool.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// [McpServerTool(Name = "calculateSum", ReadOnly = true)]
    /// public int Add(int a, int b) => a + b;
    /// 
    /// [McpServerTool(Name = "createRecord", ReadOnly = false)]
    /// public void CreateRecord(string id, string data) 
    /// {
    ///     // This modifies data, so ReadOnly = false
    ///     _database.Insert(id, data);
    /// }
    /// </code>
    /// </example>
    public bool ReadOnly  
    {
        get => _readOnly ?? ReadOnlyDefault; 
        set => _readOnly = value; 
    }
}
