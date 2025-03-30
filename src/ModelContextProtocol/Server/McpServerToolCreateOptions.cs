using System.ComponentModel;

namespace ModelContextProtocol.Server;

/// <summary>Provides options for controlling the creation of an <see cref="McpServerTool"/>.</summary>
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
    /// Gets or sets a human-readable title for the tool.
    /// </summary>
    public string? Title { get; set; }

    /// <summary>
    /// Gets or sets whether the tool may perform destructive updates to its environment.
    /// </summary>
    public bool? Destructive { get; set; }

    /// <summary>
    /// Gets or sets whether calling the tool repeatedly with the same arguments 
    /// will have no additional effect on its environment.
    /// </summary>
    public bool? Idempotent { get; set; }

    /// <summary>
    /// Gets or sets whether this tool may interact with an "open world" of external entities.
    /// </summary>
    public bool? OpenWorld { get; set; }

    /// <summary>
    /// Gets or sets whether this tool does not modify its environment.
    /// </summary>
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
