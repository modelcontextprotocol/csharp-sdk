namespace ModelContextProtocol.Server;

/// <summary>
/// Used to indicate that a method should be considered an MCP tool and describe it.
/// </summary>
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
    /// Initializes a new instance of the <see cref="McpServerToolTypeAttribute"/> class.
    /// </summary>
    public McpServerToolAttribute()
    {
    }

    /// <summary>Gets the name of the tool.</summary>
    /// <remarks>If <see langword="null"/>, the method name will be used.</remarks>
    public string? Name { get; set; }

    /// <summary>
    /// Gets or sets a human-readable title for the tool.
    /// </summary>
    public string? Title { get; set; }

    /// <summary>
    /// Gets or sets whether the tool may perform destructive updates to its environment.
    /// </summary>
    public bool Destructive 
    {
        get => _destructive ?? DestructiveDefault; 
        set => _destructive = value; 
    }

    /// <summary>
    /// Gets or sets whether calling the tool repeatedly with the same arguments will have no additional effect on its environment.
    /// </summary>
    public bool Idempotent 
    {
        get => _idempotent ?? IdempotentDefault;
        set => _idempotent = value; 
    }

    /// <summary>
    /// Gets or sets whether this tool may interact with an "open world" of external entities
    /// (e.g. the world of a web search tool is open, whereas that of a memory tool is not).
    /// </summary>
    public bool OpenWorld
    {
        get => _openWorld ?? OpenWorldDefault; 
        set => _openWorld = value; 
    }

    /// <summary>
    /// Gets or sets whether the tool does not modify its environment.
    /// </summary>
    public bool ReadOnly 
    {
        get => _readOnly ?? ReadOnlyDefault; 
        set => _readOnly = value; 
    }
}
