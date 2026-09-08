using System.Text.Json.Serialization;

namespace ModelContextProtocol.Extensions.Skills;

/// <summary>
/// Represents a single file belonging to a skill, as listed in a <see cref="Skill.Resources"/> manifest.
/// </summary>
/// <remarks>
/// <para>
/// This is distinct from <see cref="ModelContextProtocol.Protocol.Resource"/>, the base protocol's resource
/// metadata type. A <see cref="SkillResource"/> carries only the integrity information a host needs to verify
/// a skill's file: its URI, digest, and size.
/// </para>
/// <para>
/// See the <see href="https://github.com/modelcontextprotocol/ext-skills/blob/main/specification/stable/skills.mdx">Skills extension specification</see>
/// for details.
/// </para>
/// </remarks>
public sealed class SkillResource
{
    /// <summary>
    /// Gets or sets the resource URI of the file, readable via <c>resources/read</c>.
    /// </summary>
    [JsonPropertyName("uri")]
    public required string Uri { get; set; }

    /// <summary>
    /// Gets or sets the SHA-256 digest of the file's raw bytes, formatted as <c>sha256:{hex}</c> where
    /// <c>{hex}</c> is 64 lowercase hexadecimal characters.
    /// </summary>
    /// <remarks>
    /// Digests are unsigned and supplied by the same server that supplies the content. A match proves the
    /// manifest and the content are consistent; it is not a security boundary and must not be treated as one.
    /// </remarks>
    [JsonPropertyName("digest")]
    public required string Digest { get; set; }

    /// <summary>
    /// Gets or sets the length in bytes of the file's raw content, being the same bytes <see cref="Digest"/> covers.
    /// </summary>
    /// <remarks>
    /// A read whose byte length differs from this value is a verification failure equivalent to a digest mismatch.
    /// </remarks>
    [JsonPropertyName("size")]
    public required long Size { get; set; }
}
