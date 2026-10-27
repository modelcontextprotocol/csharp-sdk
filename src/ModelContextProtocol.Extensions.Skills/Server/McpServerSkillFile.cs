using System.Text;

namespace ModelContextProtocol.Extensions.Skills;

/// <summary>
/// Represents one file of a skill authored through <see cref="McpServerSkill"/>.
/// </summary>
public sealed class McpServerSkillFile
{
    /// <summary>
    /// Gets the file's path relative to the skill's root directory, using <c>/</c> as the separator
    /// (for example, <c>SKILL.md</c> or <c>references/GUIDE.md</c>).
    /// </summary>
    public required string Path { get; init; }

    /// <summary>
    /// Gets the file's raw content. Its digest and size in the skill's manifest are computed from exactly these bytes.
    /// </summary>
    public required ReadOnlyMemory<byte> Content { get; init; }

    /// <summary>
    /// Gets the MIME type to advertise for the file's resource, or <see langword="null"/> to infer one from the
    /// file's extension.
    /// </summary>
    public string? MimeType { get; init; }

    /// <summary>
    /// Creates a file from UTF-8 encoded text.
    /// </summary>
    /// <param name="path">The file's path relative to the skill's root directory.</param>
    /// <param name="text">The file's content.</param>
    /// <param name="mimeType">The MIME type to advertise, or <see langword="null"/> to infer one from <paramref name="path"/>.</param>
    /// <returns>The file.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> or <paramref name="text"/> is <see langword="null"/>.</exception>
    public static McpServerSkillFile FromText(string path, string text, string? mimeType = null)
    {
#if NET
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(text);
#else
        if (path is null) throw new ArgumentNullException(nameof(path));
        if (text is null) throw new ArgumentNullException(nameof(text));
#endif

        return new McpServerSkillFile
        {
            Path = path,
            Content = Encoding.UTF8.GetBytes(text),
            MimeType = mimeType,
        };
    }
}
