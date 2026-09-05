namespace ModelContextProtocol.Extensions.Skills;

/// <summary>
/// Provides constants for the MCP Skills extension (SEP-2640).
/// </summary>
public static class SkillsProtocol
{
    /// <summary>
    /// The extension identifier for the MCP Skills extension.
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
    /// The name of the optional request method sent from the client to list the direct children
    /// of a directory resource.
    /// </summary>
    public const string MethodResourcesDirectoryRead = "resources/directory/read";

    /// <summary>
    /// The name of the capability setting indicating that the server implements
    /// <see cref="MethodResourcesDirectoryRead"/>.
    /// </summary>
    public const string DirectoryReadSetting = "directoryRead";

    /// <summary>
    /// The reverse-domain prefix reserved by this extension for <c>_meta</c> keys on skill resources.
    /// </summary>
    public const string MetaPrefix = "io.modelcontextprotocol.skills/";

    /// <summary>
    /// The file name of the required skill manifest at the root of every skill directory.
    /// </summary>
    public const string SkillManifestFileName = "SKILL.md";

    /// <summary>
    /// The MIME type identifying a directory resource.
    /// </summary>
    public const string DirectoryMimeType = "inode/directory";

    /// <summary>
    /// The MIME type recommended for a skill's <c>SKILL.md</c> resource.
    /// </summary>
    public const string SkillManifestMimeType = "text/markdown";

    /// <summary>
    /// The sentinel used in place of a resource array when a skill's content is generated dynamically.
    /// </summary>
    public const string DynamicResourcesSentinel = "dynamic";

    /// <summary>
    /// The maximum number of resources a single skill may declare, per the SEP-2640 limits.
    /// </summary>
    public const int MaxResourcesPerSkill = 512;

    /// <summary>
    /// The maximum total size in bytes of a single skill's resources, per the SEP-2640 limits.
    /// </summary>
    public const long MaxTotalSizeBytes = 16 * 1024 * 1024;
}
