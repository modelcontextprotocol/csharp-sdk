using System.Text.Json.Serialization;

namespace ModelContextProtocol.Protocol;

/// <summary>
/// Represents additional properties describing a <see cref="Tool"/> to clients.
/// </summary>
/// <remarks>
/// All properties in <see cref="ToolAnnotations"/> are hints.
/// They are not guaranteed to provide a faithful description of tool behavior (including descriptive properties like `title`).
/// Clients should never make tool use decisions based on <see cref="ToolAnnotations"/> received from untrusted servers.
/// </remarks>
public sealed class ToolAnnotations
{
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
    [JsonPropertyName("title")]
    public string? Title { get; set; }

    /// <summary>
    /// Gets or sets a value that indicates whether the tool can perform destructive updates to its environment.
    /// </summary>
    /// <value>
    /// <see langword="true"/> if the tool can perform destructive updates to its environment;
    /// <see langword="false"/> if the tool performs only additive updates;
    /// <see langword="null"/> if unspecified, in which case clients should assume <see langword="true"/>.
    /// </value>
    /// <remarks>
    /// This property is most relevant when the tool modifies its environment (ReadOnly = false).
    /// </remarks>
    [JsonPropertyName("destructiveHint")]
    public bool? DestructiveHint { get; set; }

    /// <summary>
    /// Gets or sets a value that indicates whether calling the tool repeatedly with the same arguments
    /// has no additional effect on its environment.
    /// </summary>
    /// <value>
    /// <see langword="true"/> if calling the tool repeatedly with the same arguments
    /// has no additional effect on the environment; <see langword="false"/> if it does;
    /// <see langword="null"/> if unspecified, in which case clients should assume <see langword="false"/>.
    /// </value>
    /// <remarks>
    /// This property is most relevant when the tool modifies its environment (ReadOnly = false).
    /// </remarks>
    [JsonPropertyName("idempotentHint")]
    public bool? IdempotentHint { get; set; }

    /// <summary>
    /// Gets or sets a value that indicates whether this tool can interact with an "open world" of external entities.
    /// </summary>
    /// <value>
    /// <see langword="true"/> if the tool can interact with an unpredictable or dynamic set of entities (like web search);
    /// <see langword="false"/> if the tool's domain of interaction is closed and well-defined (like memory access);
    /// <see langword="null"/> if unspecified, in which case clients should assume <see langword="true"/>.
    /// </value>
    [JsonPropertyName("openWorldHint")]
    public bool? OpenWorldHint { get; set; }

    /// <summary>
    /// Gets or sets a value that indicates whether this tool modifies its environment.
    /// </summary>
    /// <value>
    /// <see langword="true"/> if the tool only performs read operations without changing state;
    /// <see langword="false"/> if the tool can make modifications to its environment;
    /// <see langword="null"/> if unspecified, in which case clients should assume <see langword="false"/>.
    /// </value>
    /// <remarks>
    /// <para>
    /// Read-only tools do not have side effects beyond computational resource usage.
    /// They don't create, update, or delete data in any system.
    /// </para>
    /// </remarks>
    [JsonPropertyName("readOnlyHint")]
    public bool? ReadOnlyHint { get; set; }
}
