namespace ModelContextProtocol.Extensions.Skills;

/// <summary>
/// Provides constants for the MCP Skills extension (SEP-2640).
/// </summary>
/// <remarks>
/// See the <see href="https://github.com/modelcontextprotocol/ext-skills/blob/main/specification/stable/skills.mdx">Skills extension specification</see>
/// for details.
/// </remarks>
public static class SkillsProtocol
{
    /// <summary>
    /// The extension identifier for the MCP Skills extension, as it appears in the <c>extensions</c>
    /// field of a server's capabilities.
    /// </summary>
    public const string ExtensionId = "io.modelcontextprotocol/skills";

    /// <summary>
    /// The name of the request method sent from the client to enumerate the skills a server serves.
    /// </summary>
    public const string MethodSkillsList = "skills/list";

    /// <summary>
    /// The name of the request method sent from the client to retrieve a single skill's entry by URI.
    /// </summary>
    public const string MethodSkillsGet = "skills/get";

    /// <summary>
    /// The file name of the manifest every skill directory must contain at its root.
    /// </summary>
    public const string SkillFileName = "SKILL.md";

    /// <summary>
    /// The maximum number of files a single skill may declare in its manifest, <see cref="SkillFileName"/> included.
    /// </summary>
    /// <remarks>
    /// Hosts must support skills up to and including this limit. Servers should not serve a skill that exceeds it.
    /// </remarks>
    public const int MaxResourcesPerSkill = 512;

    /// <summary>
    /// The maximum total size in bytes of a single skill's files, summed over its manifest.
    /// </summary>
    /// <remarks>
    /// Hosts must support skills up to and including this limit. Servers should not serve a skill that exceeds it.
    /// </remarks>
    public const long MaxTotalSizeBytes = 16 * 1024 * 1024;

    /// <summary>The value of a skill's <c>resources</c> field when its content is generated dynamically.</summary>
    internal const string DynamicResourcesSentinel = "dynamic";

    /// <summary>The prefix of a manifest digest.</summary>
    internal const string DigestPrefix = "sha256:";

    /// <summary>The MIME type recommended for a skill's <c>SKILL.md</c> resource.</summary>
    internal const string SkillFileMimeType = "text/markdown";
}
