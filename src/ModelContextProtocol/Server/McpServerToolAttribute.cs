using Microsoft.Extensions.DependencyInjection;

namespace ModelContextProtocol.Server;

/// <summary>
/// Used to indicate that a method should be considered an <see cref="McpServerTool"/>.
/// </summary>
/// <remarks>
/// <para>
/// This attribute is applied to methods that should be exposed as tools in the Model Context Protocol. When a class 
/// containing methods marked with this attribute is registered with <see cref="McpServerBuilderExtensions"/>,
/// these methods become available as tools that can be called by MCP clients.
/// </para>
/// <para>
/// When methods are provided directly to <see cref="M:McpServerTool.Create"/>, the attribute is not required.
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
    public string? Title { get; set; }

    /// <summary>
    /// Gets or sets whether the tool may perform destructive updates to its environment.
    /// </summary>
    /// <remarks>
    /// <para>
    /// If <see langword="true"/>, the tool may perform destructive updates to its environment.
    /// If <see langword="false"/>, the tool performs only additive updates.
    /// This property is most relevant when the tool modifies its environment (ReadOnly = false).
    /// </para>
    /// <para>
    /// The default is <see langword="true"/>.
    /// </para>
    /// </remarks>
    public bool Destructive 
    {
        get => _destructive ?? DestructiveDefault; 
        set => _destructive = value; 
    }

    /// <summary>
    /// Gets or sets whether calling the tool repeatedly with the same arguments 
    /// will have no additional effect on its environment.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This property is most relevant when the tool modifies its environment (ReadOnly = false).
    /// </para>
    /// <para>
    /// The default is <see langword="false"/>.
    /// </para>
    /// </remarks>
    public bool Idempotent  
    {
        get => _idempotent ?? IdempotentDefault;
        set => _idempotent = value; 
    }

    /// <summary>
    /// Gets or sets whether this tool may interact with an "open world" of external entities.
    /// </summary>
    /// <remarks>
    /// <para>
    /// If <see langword="true"/>, the tool may interact with an unpredictable or dynamic set of entities (like web search).
    /// If <see langword="false"/>, the tool's domain of interaction is closed and well-defined (like memory access).
    /// </para>
    /// <para>
    /// The default is <see langword="true"/>.
    /// </para>
    /// </remarks>
    public bool OpenWorld
    {
        get => _openWorld ?? OpenWorldDefault; 
        set => _openWorld = value; 
    }

    /// <summary>
    /// Gets or sets whether this tool does not modify its environment.
    /// </summary>
    /// <remarks>
    /// <para>
    /// If <see langword="true"/>, the tool only performs read operations without changing state.
    /// If <see langword="false"/>, the tool may make modifications to its environment.
    /// </para>
    /// <para>
    /// Read-only tools do not have side effects beyond computational resource usage.
    /// They don't create, update, or delete data in any system.
    /// </para>
    /// <para>
    /// The default is <see langword="false"/>.
    /// </para>
    /// </remarks>
    public bool ReadOnly  
    {
        get => _readOnly ?? ReadOnlyDefault; 
        set => _readOnly = value; 
    }
}
