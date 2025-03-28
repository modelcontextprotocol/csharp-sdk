namespace ModelContextProtocol.Server;

/// <summary>
/// Used to indicate that a method should be considered an MCP tool and describe it.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class McpServerToolAttribute : Attribute
{
    private bool? _destructive;
    private bool? _idempotent;
    private bool? _openWorld;
    private bool? _readOnly;

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
    public bool Destructive { get => _destructive ?? true; set => _destructive = value; }

    /// <summary>
    /// Gets whether the <see cref="Destructive"/> property has been assigned a value.
    /// </summary>
    public bool DestructiveIsSet => _destructive.HasValue;

    /// <summary>
    /// Gets or sets whether calling the tool repeatedly with the same arguments will have no additional effect on its environment.
    /// </summary>
    public bool Idempotent { get => _idempotent ?? false; set => _idempotent = value; }

    /// <summary>
    /// Gets whether the <see cref="Idempotent"/> property has been assigned a value.
    /// </summary>
    public bool IdempotentIsSet => _idempotent.HasValue;

    /// <summary>
    /// Gets or sets whether this tool may interact with an "open world" of external entities
    /// (e.g. the world of a web search tool is open, whereas that of a memory tool is not).
    /// </summary>
    public bool OpenWorld { get => _openWorld ?? true; set => _openWorld = value; }

    /// <summary>
    /// Gets whether the <see cref="OpenWorld"/> property has been assigned a value.
    /// </summary>
    public bool OpenWorldIsSet => _openWorld.HasValue;

    /// <summary>
    /// Gets or sets whether the tool does not modify its environment.
    /// </summary>
    public bool ReadOnly { get => _readOnly ?? false; set => _readOnly = value; }

    /// <summary>
    /// Gets whether the <see cref="ReadOnly"/> property has been assigned a value.
    /// </summary>
    public bool ReadOnlyIsSet => _readOnly.HasValue;
}
