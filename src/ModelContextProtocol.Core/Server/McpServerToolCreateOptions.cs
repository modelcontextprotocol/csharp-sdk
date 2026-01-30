using Microsoft.Extensions.AI;
using ModelContextProtocol.Protocol;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ModelContextProtocol.Server;

/// <summary>
/// Provides options for controlling the creation of an <see cref="McpServerTool"/>.
/// </summary>
/// <remarks>
/// <para>
/// These options allow for customizing the behavior and metadata of tools created with
/// <see cref="M:McpServerTool.Create"/>. They provide control over naming, description,
/// tool properties, and dependency injection integration.
/// </para>
/// <para>
/// When creating tools programmatically rather than using attributes, these options
/// provide the same level of configuration flexibility.
/// </para>
/// </remarks>
public sealed class McpServerToolCreateOptions
{
    /// <summary>
    /// Gets or sets optional services used in the construction of the <see cref="McpServerTool"/>.
    /// </summary>
    /// <remarks>
    /// These services will be used to determine which parameters should be satisfied from dependency injection. As such,
    /// what services are satisfied via this provider should match what's satisfied via the provider passed in at invocation time.
    /// </remarks>
    public IServiceProvider? Services { get; set; }

    /// <summary>
    /// Gets or sets the name to use for the <see cref="McpServerTool"/>.
    /// </summary>
    /// <remarks>
    /// If <see langword="null"/>, but an <see cref="McpServerToolAttribute"/> is applied to the method,
    /// the name from the attribute is used. If that's not present, a name based on the method's name is used.
    /// </remarks>
    public string? Name { get; set; }

    /// <summary>
    /// Gets or sets the description to use for the <see cref="McpServerTool"/>.
    /// </summary>
    /// <remarks>
    /// If <see langword="null"/>, but a <see cref="DescriptionAttribute"/> is applied to the method,
    /// the description from that attribute is used.
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
    public string? Title { get; set; }

    /// <summary>
    /// Gets or sets a value that indicates whether the tool might perform destructive updates to its environment.
    /// </summary>
    /// <value>
    /// <see langword="true"/> if the tool might perform destructive updates to its environment.
    /// <see langword="false"/> if the tool performs only additive updates.
    /// The default is <see langword="true"/>.
    /// </value>
    /// <remarks>
    /// This property is most relevant when the tool modifies its environment (ReadOnly = false).
    /// </remarks>
    public bool? Destructive { get; set; }

    /// <summary>
    /// Gets or sets a value that indicates whether calling the tool repeatedly with the same arguments
    /// has no additional effect on its environment.
    /// </summary>
    /// <value>
    /// <see langword="true"/> if calling the tool repeatedly with the same arguments
    /// has no additional effect on the environment; <see langword="false"/> if it does.
    /// The default is <see langword="false"/>.
    /// </value>
    /// <remarks>
    /// This property is most relevant when the tool modifies its environment (ReadOnly = false).
    /// </remarks>
    public bool? Idempotent { get; set; }

    /// <summary>
    /// Gets or sets a value that indicates whether this tool can interact with an "open world" of external entities.
    /// </summary>
    /// <value>
    /// <see langword="true"/> if the tool can interact with an unpredictable or dynamic set of entities (like web search).
    /// <see langword="false"/> if the tool's domain of interaction is closed and well-defined (like memory access).
    /// The default is <see langword="true"/>.
    /// </value>
    public bool? OpenWorld { get; set; }

    /// <summary>
    /// Gets or sets a value that indicates whether this tool does not modify its environment.
    /// </summary>
    /// <value>
    /// If <see langword="true"/>, the tool only performs read operations without changing state.
    /// If <see langword="false"/>, the tool might make modifications to its environment.
    /// The default is <see langword="false"/>.
    /// </value>
    /// <remarks>
    /// Read-only tools do not have side effects beyond computational resource usage.
    /// They don't create, update, or delete data in any system.
    /// </remarks>
    public bool? ReadOnly { get; set; }

    /// <summary>
    /// Gets or sets a value that indicates whether the tool should report an output schema for structured content.
    /// </summary>
    /// <value>
    /// The default is <see langword="false"/>.
    /// </value>
    /// <remarks>
    /// When enabled, the tool will attempt to populate the <see cref="Tool.OutputSchema"/>
    /// and provide structured content in the <see cref="CallToolResult.StructuredContent"/> property.
    /// </remarks>
    public bool UseStructuredContent { get; set; }

    /// <summary>
    /// Gets or sets the JSON serializer options to use when marshalling data to/from JSON.
    /// </summary>
    /// <value>
    /// The default is <see cref="McpJsonUtilities.DefaultOptions"/>.
    /// </value>
    public JsonSerializerOptions? SerializerOptions { get; set; }

    /// <summary>
    /// Gets or sets the JSON schema options when creating an <see cref="AIFunction"/> from a method.
    /// </summary>
    /// <value>
    /// The default is <see cref="AIJsonSchemaCreateOptions.Default"/>.
    /// </value>
    public AIJsonSchemaCreateOptions? SchemaCreateOptions { get; set; }

    /// <summary>
    /// Gets or sets the metadata associated with the tool.
    /// </summary>
    /// <remarks>
    /// Metadata includes information such as attributes extracted from the method and its declaring class.
    /// If not provided, metadata will be automatically generated for methods created via reflection.
    /// </remarks>
    public IReadOnlyList<object>? Metadata { get; set; }

    /// <summary>
    /// Gets or sets the icons for this tool.
    /// </summary>
    /// <remarks>
    /// This property can be used by clients to display the tool's icon in a user interface.
    /// </remarks>
    public IList<Icon>? Icons { get; set; }

    /// <summary>
    /// Gets or sets metadata reserved by MCP for protocol-level metadata.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This <see cref="JsonObject"/> is used to seed the <see cref="Tool.Meta"/> property. Any metadata from
    /// <see cref="McpMetaAttribute"/> instances on the method will be added to this object, but
    /// properties already present in this <see cref="JsonObject"/> will not be overwritten.
    /// </para>
    /// <para>
    /// Implementations must not make assumptions about its contents.
    /// </para>
    /// </remarks>
    public JsonObject? Meta { get; set; }

    /// <summary>
    /// Gets or sets the execution hints for this tool.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Execution hints provide information about how the tool should be invoked, including
    /// task support level (<see cref="ToolTaskSupport"/>).
    /// </para>
    /// <para>
    /// If <see langword="null"/>, the tool's execution settings are determined automatically based on
    /// the method signature (async methods get <see cref="ToolTaskSupport.Optional"/>; sync methods
    /// get <see cref="ToolTaskSupport.Forbidden"/>).
    /// </para>
    /// </remarks>
    [Experimental(Experimentals.Tasks_DiagnosticId, UrlFormat = Experimentals.Tasks_Url)]
    public ToolExecution? Execution { get; set; }

    /// <summary>
    /// Creates a shallow clone of the current <see cref="McpServerToolCreateOptions"/> instance.
    /// </summary>
    internal McpServerToolCreateOptions Clone() =>
        new()
        {
            Services = Services,
            Name = Name,
            Description = Description,
            Title = Title,
            Destructive = Destructive,
            Idempotent = Idempotent,
            OpenWorld = OpenWorld,
            ReadOnly = ReadOnly,
            UseStructuredContent = UseStructuredContent,
            SerializerOptions = SerializerOptions,
            SchemaCreateOptions = SchemaCreateOptions,
            Metadata = Metadata,
            Icons = Icons,
            Meta = Meta,
            Execution = Execution,
        };
}
