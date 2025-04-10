using System.Text.Json.Serialization;

namespace ModelContextProtocol.Protocol.Types;

/// <summary>
/// Represents additional properties describing a <see cref="Tool"/> to clients.
/// </summary>
/// <remarks>
/// All properties in <see cref="ToolAnnotations"/> are hints.
/// They are not guaranteed to provide a faithful description of tool behavior (including descriptive properties like `title`).
/// Clients should never make tool use decisions based on <see cref="ToolAnnotations"/> received from untrusted servers.
/// </remarks>
public class ToolAnnotations
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
    [JsonPropertyName("destructiveHint")]
    public bool? DestructiveHint { get; set; }

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
    [JsonPropertyName("idempotentHint")]
    public bool? IdempotentHint { get; set; }

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
    [JsonPropertyName("openWorldHint")]
    public bool? OpenWorldHint { get; set; }

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
    [JsonPropertyName("readOnlyHint")]
    public bool? ReadOnlyHint { get; set; }
}