namespace ModelContextProtocol.Extensions.Skills;

/// <summary>Provides options for creating a server-side skill.</summary>
public sealed class McpServerSkillCreateOptions
{
    /// <summary>Gets or sets the server-local metadata associated with the skill primitive.</summary>
    /// <remarks>
    /// Creation takes a shallow, read-only snapshot of this list. Metadata is not serialized, interpreted as
    /// authorization, or copied to file resources. Files, especially files shared between skills, require
    /// their own access policy. Metadata objects remain caller-owned.
    /// </remarks>
    public IReadOnlyList<object>? Metadata { get; set; }
}
