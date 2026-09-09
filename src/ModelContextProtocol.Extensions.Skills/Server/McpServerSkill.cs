using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.Text;
using System.Text.Json.Nodes;

namespace ModelContextProtocol.Extensions.Skills;

/// <summary>
/// Represents a skill served by an MCP server: its <see cref="Skill"/> entry together with the
/// <see cref="McpServerResource"/> instances that serve the skill's files.
/// </summary>
/// <remarks>
/// <para>
/// The specification requires a skill's manifest to carry the SHA-256 digest and size of every file, and a host
/// refuses content whose bytes do not match. Building a skill through <see cref="Create(IEnumerable{McpServerSkillFile})"/>
/// or <see cref="CreateFromDirectory(string)"/> (or their overloads) computes the manifest from the same bytes the
/// resources serve, so the two cannot disagree.
/// </para>
/// <para>
/// The skill's frontmatter is read from its <c>SKILL.md</c> by <see cref="SkillFrontmatter"/>. Hosts re-parse the
/// fetched <c>SKILL.md</c> and compare it field by field against the published entry, treating any discrepancy as
/// a verification failure, so the two must agree. The overloads that accept an explicit <see cref="JsonObject"/>
/// exist for frontmatter the reader cannot handle; when it can read the file, an explicit object that differs from
/// it is rejected.
/// </para>
/// <para>
/// Register skills with <see cref="McpSkillsBuilderExtensions.WithSkills(IMcpServerBuilder, IEnumerable{McpServerSkill}, Action{McpSkillsOptions})"/>,
/// which registers both the catalog entries and the file resources, or point
/// <see cref="McpSkillsBuilderExtensions.WithSkillsFromDirectory(IMcpServerBuilder, string, string, Action{McpSkillsOptions})"/>
/// at a directory of skills.
/// </para>
/// </remarks>
public sealed class McpServerSkill
{
    private McpServerSkill(Skill protocolSkill, IReadOnlyList<McpServerResource> resources)
    {
        ProtocolSkill = protocolSkill;
        Resources = resources;
    }

    /// <summary>
    /// Gets the skill's entry, as returned by <c>skills/list</c> and <c>skills/get</c>.
    /// </summary>
    /// <remarks>
    /// The catalog that <c>WithSkills</c> creates keeps its own copy of this entry, so changes made to this object
    /// after registration do not affect what is served.
    /// </remarks>
    public Skill ProtocolSkill { get; }

    /// <summary>
    /// Gets the resources serving the skill's files, one per file, each addressable at the URI its manifest entry names.
    /// </summary>
    public IReadOnlyList<McpServerResource> Resources { get; }

    /// <summary>
    /// Creates a skill from its files, reading the frontmatter from <c>SKILL.md</c> and deriving the skill's URI
    /// from the frontmatter's <c>name</c> as <c>skill://{name}/SKILL.md</c>.
    /// </summary>
    /// <param name="files">The skill's files. Exactly one must have the path <c>SKILL.md</c>.</param>
    /// <returns>The skill.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="files"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="files"/> omits <c>SKILL.md</c>, contains a duplicate or unsafe path, or exceeds the
    /// specification's per-skill limits; or the <c>SKILL.md</c> frontmatter cannot be read (see
    /// <see cref="SkillFrontmatter"/>) or is missing a required field.
    /// </exception>
    public static McpServerSkill Create(IEnumerable<McpServerSkillFile> files)
    {
#if NET
        ArgumentNullException.ThrowIfNull(files);
#else
        if (files is null) throw new ArgumentNullException(nameof(files));
#endif

        return CreateCore(uri: null, uriPrefix: DefaultUriPrefix, frontmatter: null, files, nameof(files));
    }

    /// <summary>
    /// Creates a skill from its files, reading the frontmatter from <c>SKILL.md</c>.
    /// </summary>
    /// <param name="uri">
    /// The resource URI of the skill's <c>SKILL.md</c>, for example <c>skill://git-workflow/SKILL.md</c>. The path
    /// segment preceding <c>/SKILL.md</c> must equal the skill's name.
    /// </param>
    /// <param name="files">The skill's files. Exactly one must have the path <c>SKILL.md</c>.</param>
    /// <returns>The skill.</returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="uri"/> does not end in <c>/SKILL.md</c>; <paramref name="files"/> omits <c>SKILL.md</c>,
    /// contains a duplicate or unsafe path, or exceeds the specification's per-skill limits; or the <c>SKILL.md</c>
    /// frontmatter cannot be read (see <see cref="SkillFrontmatter"/>), is missing a required field, or has a
    /// name that does not match <paramref name="uri"/>.
    /// </exception>
    public static McpServerSkill Create(string uri, IEnumerable<McpServerSkillFile> files)
    {
#if NET
        ArgumentNullException.ThrowIfNull(uri);
        ArgumentNullException.ThrowIfNull(files);
#else
        if (uri is null) throw new ArgumentNullException(nameof(uri));
        if (files is null) throw new ArgumentNullException(nameof(files));
#endif

        return CreateCore(uri, uriPrefix: null, frontmatter: null, files, nameof(files));
    }

    /// <summary>
    /// Creates a skill from its files with explicitly supplied frontmatter.
    /// </summary>
    /// <param name="uri">
    /// The resource URI of the skill's <c>SKILL.md</c>, for example <c>skill://git-workflow/SKILL.md</c>. The path
    /// segment preceding <c>/SKILL.md</c> must equal the skill's name.
    /// </param>
    /// <param name="frontmatter">
    /// The <c>SKILL.md</c> YAML frontmatter rendered as a JSON object. It must contain string <c>name</c> and
    /// <c>description</c> fields and reproduce the authored frontmatter exactly.
    /// </param>
    /// <param name="files">The skill's files. Exactly one must have the path <c>SKILL.md</c>.</param>
    /// <returns>The skill.</returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="uri"/> does not end in <c>/SKILL.md</c>; <paramref name="frontmatter"/> is missing a required
    /// field, has a name that does not match <paramref name="uri"/>, or differs from the frontmatter in
    /// <c>SKILL.md</c>; or <paramref name="files"/> omits <c>SKILL.md</c>, contains a duplicate or unsafe path, or
    /// exceeds the specification's per-skill limits.
    /// </exception>
    /// <remarks>
    /// Prefer <see cref="Create(string, IEnumerable{McpServerSkillFile})"/>, which reads the frontmatter from the
    /// file. This overload exists for frontmatter that <see cref="SkillFrontmatter"/> cannot read. When it can read
    /// the file, the supplied object must match it exactly, since hosts compare the two field by field and refuse
    /// the skill on any difference.
    /// </remarks>
    public static McpServerSkill Create(string uri, JsonObject frontmatter, IEnumerable<McpServerSkillFile> files)
    {
#if NET
        ArgumentNullException.ThrowIfNull(uri);
        ArgumentNullException.ThrowIfNull(frontmatter);
        ArgumentNullException.ThrowIfNull(files);
#else
        if (uri is null) throw new ArgumentNullException(nameof(uri));
        if (frontmatter is null) throw new ArgumentNullException(nameof(frontmatter));
        if (files is null) throw new ArgumentNullException(nameof(files));
#endif

        return CreateCore(uri, uriPrefix: null, frontmatter, files, nameof(files));
    }

    /// <summary>
    /// Creates a skill from every file in a directory, recursively, reading the frontmatter from its <c>SKILL.md</c>
    /// and deriving the skill's URI from the frontmatter's <c>name</c> as <c>skill://{name}/SKILL.md</c>.
    /// </summary>
    /// <param name="directoryPath">The skill's root directory. It must contain a <c>SKILL.md</c>.</param>
    /// <returns>The skill.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="directoryPath"/> is <see langword="null"/>.</exception>
    /// <exception cref="DirectoryNotFoundException"><paramref name="directoryPath"/> does not exist.</exception>
    /// <exception cref="ArgumentException">
    /// The directory's contents do not form a valid skill (see <see cref="Create(IEnumerable{McpServerSkillFile})"/>),
    /// or the directory contains a symbolic link or other reparse point.
    /// </exception>
    /// <remarks>
    /// <para>
    /// Files are read once, when this method is called. Changes on disk afterwards are not reflected in the
    /// manifest or the served content.
    /// </para>
    /// <para>
    /// Links are not followed. A symbolic link inside the directory could point outside it and publish a file
    /// under a URI that appears to belong to the skill, so encountering one is an error. Replace the link with a
    /// regular file or directory, or build the skill with <see cref="Create(IEnumerable{McpServerSkillFile})"/> and
    /// explicit files.
    /// </para>
    /// </remarks>
    public static McpServerSkill CreateFromDirectory(string directoryPath)
    {
#if NET
        ArgumentNullException.ThrowIfNull(directoryPath);
#else
        if (directoryPath is null) throw new ArgumentNullException(nameof(directoryPath));
#endif

        return CreateCore(uri: null, uriPrefix: DefaultUriPrefix, frontmatter: null, ReadDirectory(directoryPath), nameof(directoryPath));
    }

    /// <summary>
    /// Creates a skill from every file in a directory, recursively, reading the frontmatter from its <c>SKILL.md</c>.
    /// </summary>
    /// <param name="uri">
    /// The resource URI of the skill's <c>SKILL.md</c>, for example <c>skill://git-workflow/SKILL.md</c>. The path
    /// segment preceding <c>/SKILL.md</c> must equal the skill's name.
    /// </param>
    /// <param name="directoryPath">The skill's root directory. It must contain a <c>SKILL.md</c>.</param>
    /// <returns>The skill.</returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    /// <exception cref="DirectoryNotFoundException"><paramref name="directoryPath"/> does not exist.</exception>
    /// <exception cref="ArgumentException">
    /// The directory's contents do not form a valid skill (see <see cref="Create(string, IEnumerable{McpServerSkillFile})"/>),
    /// or the directory contains a symbolic link or other reparse point.
    /// </exception>
    /// <remarks>
    /// See <see cref="CreateFromDirectory(string)"/> for how files and links are handled.
    /// </remarks>
    public static McpServerSkill CreateFromDirectory(string uri, string directoryPath)
    {
#if NET
        ArgumentNullException.ThrowIfNull(uri);
        ArgumentNullException.ThrowIfNull(directoryPath);
#else
        if (uri is null) throw new ArgumentNullException(nameof(uri));
        if (directoryPath is null) throw new ArgumentNullException(nameof(directoryPath));
#endif

        return CreateCore(uri, uriPrefix: null, frontmatter: null, ReadDirectory(directoryPath), nameof(directoryPath));
    }

    /// <summary>
    /// Creates a skill from every file in a directory, recursively, with explicitly supplied frontmatter.
    /// </summary>
    /// <param name="uri">
    /// The resource URI of the skill's <c>SKILL.md</c>, for example <c>skill://git-workflow/SKILL.md</c>. The path
    /// segment preceding <c>/SKILL.md</c> must equal the skill's name.
    /// </param>
    /// <param name="frontmatter">
    /// The <c>SKILL.md</c> YAML frontmatter rendered as a JSON object. It must contain string <c>name</c> and
    /// <c>description</c> fields and reproduce the authored frontmatter exactly.
    /// </param>
    /// <param name="directoryPath">The skill's root directory. It must contain a <c>SKILL.md</c>.</param>
    /// <returns>The skill.</returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    /// <exception cref="DirectoryNotFoundException"><paramref name="directoryPath"/> does not exist.</exception>
    /// <exception cref="ArgumentException">
    /// The directory's contents do not form a valid skill (see <see cref="Create(string, JsonObject, IEnumerable{McpServerSkillFile})"/>),
    /// or the directory contains a symbolic link or other reparse point.
    /// </exception>
    /// <remarks>
    /// Prefer <see cref="CreateFromDirectory(string, string)"/>, which reads the frontmatter from the file. This
    /// overload exists for frontmatter that <see cref="SkillFrontmatter"/> cannot read. When it can read the file,
    /// the supplied object must match it exactly. See <see cref="CreateFromDirectory(string)"/> for how files and
    /// links are handled.
    /// </remarks>
    public static McpServerSkill CreateFromDirectory(string uri, JsonObject frontmatter, string directoryPath)
    {
#if NET
        ArgumentNullException.ThrowIfNull(uri);
        ArgumentNullException.ThrowIfNull(frontmatter);
        ArgumentNullException.ThrowIfNull(directoryPath);
#else
        if (uri is null) throw new ArgumentNullException(nameof(uri));
        if (frontmatter is null) throw new ArgumentNullException(nameof(frontmatter));
        if (directoryPath is null) throw new ArgumentNullException(nameof(directoryPath));
#endif

        return CreateCore(uri, uriPrefix: null, frontmatter, ReadDirectory(directoryPath), nameof(directoryPath));
    }

    private const string DefaultUriPrefix = "skill://";

    /// <summary>
    /// Creates a skill from a directory, deriving its URI as <c>{uriPrefix}{name}/SKILL.md</c>. Used by
    /// <c>WithSkillsFromDirectory</c>.
    /// </summary>
    internal static McpServerSkill CreateFromDirectory(string directoryPath, string uriPrefix, string paramName) =>
        CreateCore(uri: null, uriPrefix, frontmatter: null, ReadDirectory(directoryPath), paramName);

    private static McpServerSkill CreateCore(string? uri, string? uriPrefix, JsonObject? frontmatter, IEnumerable<McpServerSkillFile> files, string filesParamName)
    {
        // Normalize and order the files: SKILL.md first, then the rest by path, so the manifest is deterministic.
        var normalized = new List<(string Path, McpServerSkillFile File)>();
        var seenPaths = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in files)
        {
            if (file is null)
            {
                throw new ArgumentException("The skill's files must not contain null entries.", filesParamName);
            }

            string path = NormalizePath(file.Path);
            if (!seenPaths.Add(path))
            {
                throw new ArgumentException($"The skill's files contain the path '{path}' more than once.", filesParamName);
            }

            normalized.Add((path, file));
        }

        if (!seenPaths.Contains(SkillsProtocol.SkillFileName))
        {
            throw new ArgumentException($"The skill's files must include '{SkillsProtocol.SkillFileName}' at the skill's root.", filesParamName);
        }

        normalized.Sort(static (left, right) =>
        {
            bool leftIsSkillFile = left.Path == SkillsProtocol.SkillFileName;
            bool rightIsSkillFile = right.Path == SkillsProtocol.SkillFileName;
            if (leftIsSkillFile != rightIsSkillFile)
            {
                return leftIsSkillFile ? -1 : 1;
            }

            return string.CompareOrdinal(left.Path, right.Path);
        });

        // Snapshot every file's bytes once. The caller's ReadOnlyMemory<byte> may alias an array the caller goes
        // on to mutate, and the digest published in the manifest must describe exactly the bytes served.
        var contents = new byte[normalized.Count][];
        for (int i = 0; i < normalized.Count; i++)
        {
            contents[i] = normalized[i].File.Content.ToArray();
        }

        // Read the frontmatter from SKILL.md (always first after sorting). When the caller supplied frontmatter,
        // the file is still read so that the two can be checked against each other. The caller's frontmatter
        // stands on its own only when the file uses valid YAML the reader does not support; a file that is not
        // UTF-8, has no frontmatter block, or is malformed cannot be reproduced by any host and is rejected.
        if (!TryDecodeUtf8(contents[0], out string? skillMarkdown))
        {
            throw new ArgumentException($"{SkillsProtocol.SkillFileName} is not valid UTF-8.", filesParamName);
        }

        JsonObject? fileFrontmatter = null;
        try
        {
            fileFrontmatter = SkillFrontmatter.Parse(skillMarkdown!);
        }
        catch (SkillFrontmatter.UnsupportedYamlException e) when (frontmatter is null)
        {
            throw new ArgumentException(
                $"The frontmatter of {SkillsProtocol.SkillFileName} uses YAML that {nameof(SkillFrontmatter)} does not support: {e.Message} " +
                "Supply the frontmatter explicitly with the overload that takes a JsonObject.",
                filesParamName);
        }
        catch (SkillFrontmatter.UnsupportedYamlException)
        {
            // Explicit frontmatter covers this case.
        }
        catch (FormatException e)
        {
            throw new ArgumentException($"The frontmatter of {SkillsProtocol.SkillFileName} could not be read: {e.Message}", filesParamName);
        }

        if (frontmatter is null)
        {
            frontmatter = fileFrontmatter!;
        }
        else if (fileFrontmatter is not null && !JsonNode.DeepEquals(fileFrontmatter, frontmatter))
        {
            throw new ArgumentException(
                $"The supplied frontmatter does not match the frontmatter in {SkillsProtocol.SkillFileName}. Hosts compare the two field by field " +
                "and refuse the skill on any difference. Fix the mismatch, or omit the frontmatter argument to have it read from the file. " +
                $"From the file: {fileFrontmatter.ToJsonString()} Supplied: {frontmatter.ToJsonString()}",
                nameof(frontmatter));
        }

        if (uri is null)
        {
            string? name = new Skill { Uri = string.Empty, Frontmatter = frontmatter, Resources = SkillResources.Dynamic }.Name;
            if (string.IsNullOrEmpty(name))
            {
                throw new ArgumentException(
                    $"The frontmatter of {SkillsProtocol.SkillFileName} must declare a string 'name' for the skill's URI to be derived from it.",
                    filesParamName);
            }

            uri = uriPrefix + name + "/" + SkillsProtocol.SkillFileName;
        }

        string root = SkillValidation.GetSkillRoot(uri, nameof(uri));

        // Build and validate the entry before creating any resources, so an invalid skill fails fast with a
        // message about the entry rather than about a resource.
        var manifest = new List<SkillResource>(normalized.Count);
        for (int i = 0; i < normalized.Count; i++)
        {
            manifest.Add(new SkillResource
            {
                Uri = root + "/" + EscapePath(normalized[i].Path),
                Digest = SkillVerifier.ComputeDigest(contents[i]),
                Size = contents[i].Length,
            });
        }

        var skill = new Skill
        {
            Uri = uri,
            Frontmatter = frontmatter,
            Resources = SkillResources.FromResources(manifest),
        };

        SkillValidation.Validate(skill, nameof(frontmatter));

        var resources = new McpServerResource[normalized.Count];
        for (int i = 0; i < normalized.Count; i++)
        {
            var (path, file) = normalized[i];
            bool isSkillFile = path == SkillsProtocol.SkillFileName;
            resources[i] = CreateResource(
                manifest[i].Uri,
                name: isSkillFile ? skill.Name! : path,
                description: isSkillFile ? skill.Description : null,
                mimeType: file.MimeType ?? (isSkillFile ? SkillsProtocol.SkillFileMimeType : GuessMimeType(path, contents[i])),
                contents[i]);
        }

        return new McpServerSkill(skill, resources);
    }

    private static List<McpServerSkillFile> ReadDirectory(string directoryPath)
    {
        string fullDirectory = Path.GetFullPath(directoryPath);
        if (!Directory.Exists(fullDirectory))
        {
            throw new DirectoryNotFoundException($"The skill directory '{fullDirectory}' does not exist.");
        }

        if ((File.GetAttributes(fullDirectory) & FileAttributes.ReparsePoint) != 0)
        {
            throw new ArgumentException(
                $"'{fullDirectory}' is a symbolic link or other reparse point. Links are not followed when loading a skill directory, " +
                "because a link can point outside the intended location. Pass the target directory instead.",
                nameof(directoryPath));
        }

        if (!fullDirectory.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal))
        {
            fullDirectory += Path.DirectorySeparatorChar;
        }

        var files = new List<McpServerSkillFile>();
        CollectFiles(fullDirectory, fullDirectory, files);
        return files;
    }

    /// <summary>
    /// Walks a skill directory without following links. A symbolic link (or any other reparse point) can point
    /// outside the skill directory, and a file reached through one would be published under a URI that looks like
    /// it lives inside the skill. Rather than try to decide which link targets are acceptable, links are rejected.
    /// </summary>
    private static void CollectFiles(string root, string directory, List<McpServerSkillFile> files)
    {
        foreach (string entry in Directory.EnumerateFileSystemEntries(directory))
        {
            FileAttributes attributes = File.GetAttributes(entry);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new ArgumentException(
                    $"'{entry}' is a symbolic link or other reparse point. Links are not followed when loading a skill directory, " +
                    $"because a link can point outside the skill. Replace it with a regular file or directory, or build the skill " +
                    $"with {nameof(McpServerSkill)}.{nameof(Create)} and explicit files.",
                    "directoryPath");
            }

            if ((attributes & FileAttributes.Directory) != 0)
            {
                CollectFiles(root, entry, files);
                continue;
            }

            string relativePath = entry.Substring(root.Length).Replace(Path.DirectorySeparatorChar, '/');
            if (Path.AltDirectorySeparatorChar != Path.DirectorySeparatorChar)
            {
                relativePath = relativePath.Replace(Path.AltDirectorySeparatorChar, '/');
            }

            files.Add(new McpServerSkillFile
            {
                Path = relativePath,
                Content = File.ReadAllBytes(entry),
            });
        }
    }

    /// <summary>
    /// Percent-encodes each segment of a normalized relative path so that characters with URI syntax (such as
    /// <c>{</c>, <c>?</c>, <c>#</c>, or a space) in a file name stay literal. Without this, a file named
    /// <c>{name}.md</c> would register as a resource template rather than a concrete resource.
    /// </summary>
    private static string EscapePath(string normalizedPath)
    {
        string[] segments = normalizedPath.Split('/');
        for (int i = 0; i < segments.Length; i++)
        {
            segments[i] = Uri.EscapeDataString(segments[i]);
        }

        return string.Join("/", segments);
    }

    private static string NormalizePath(string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            throw new ArgumentException("A skill file must have a non-empty path.", nameof(McpServerSkillFile.Path));
        }

        string normalized = path!.Replace('\\', '/');
        if (normalized.StartsWith("./", StringComparison.Ordinal))
        {
            normalized = normalized.Substring(2);
        }

        if (normalized.Length == 0 || normalized[0] == '/' || normalized[normalized.Length - 1] == '/')
        {
            throw new ArgumentException($"The skill file path '{path}' must be relative to the skill's root and must not end in a separator.", nameof(McpServerSkillFile.Path));
        }

        foreach (string segment in normalized.Split('/'))
        {
            if (segment.Length == 0 || segment == "." || segment == "..")
            {
                throw new ArgumentException($"The skill file path '{path}' must not contain empty, '.', or '..' segments.", nameof(McpServerSkillFile.Path));
            }
        }

        return normalized;
    }

    private static McpServerResource CreateResource(string uri, string name, string? description, string mimeType, ReadOnlyMemory<byte> content)
    {
        // Text is served as TextResourceContents only when the bytes round-trip through UTF-8 exactly, because the
        // host hashes the UTF-8 encoding of the text it receives and compares it against the manifest digest, which
        // was computed over the raw bytes. Anything else is served as a blob, which round-trips by construction.
        ResourceContents Read() =>
            IsTextualMimeType(mimeType) && TryDecodeUtf8(content.Span, out string? text)
                ? new TextResourceContents { Uri = uri, MimeType = mimeType, Text = text! }
                : BlobResourceContents.FromBytes(content, uri, mimeType);

        return McpServerResource.Create(Read, new McpServerResourceCreateOptions
        {
            UriTemplate = uri,
            Name = name,
            Description = description,
            MimeType = mimeType,
        });
    }

    private static bool TryDecodeUtf8(ReadOnlySpan<byte> bytes, out string? text)
    {
        try
        {
            text = s_strictUtf8.GetString(bytes.ToArray());
            return true;
        }
        catch (DecoderFallbackException)
        {
            text = null;
            return false;
        }
    }

    private static readonly UTF8Encoding s_strictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private static bool IsTextualMimeType(string mimeType) =>
        mimeType.StartsWith("text/", StringComparison.OrdinalIgnoreCase) ||
        mimeType.EndsWith("+json", StringComparison.OrdinalIgnoreCase) ||
        mimeType.EndsWith("+xml", StringComparison.OrdinalIgnoreCase) ||
        mimeType.Equals("application/json", StringComparison.OrdinalIgnoreCase) ||
        mimeType.Equals("application/xml", StringComparison.OrdinalIgnoreCase) ||
        mimeType.Equals("application/yaml", StringComparison.OrdinalIgnoreCase) ||
        mimeType.Equals("application/toml", StringComparison.OrdinalIgnoreCase) ||
        mimeType.Equals("application/javascript", StringComparison.OrdinalIgnoreCase) ||
        mimeType.Equals("application/x-sh", StringComparison.OrdinalIgnoreCase);

    private static string GuessMimeType(string path, ReadOnlySpan<byte> content)
    {
        int dot = path.LastIndexOf('.');
        string extension = dot < 0 || dot < path.LastIndexOf('/') ? string.Empty : path.Substring(dot + 1).ToLowerInvariant();

        return extension switch
        {
            "md" or "markdown" => "text/markdown",
            "txt" => "text/plain",
            "csv" => "text/csv",
            "html" or "htm" => "text/html",
            "css" => "text/css",
            "js" or "mjs" => "text/javascript",
            "ts" => "text/typescript",
            "py" => "text/x-python",
            "cs" => "text/x-csharp",
            "sh" or "bash" => "application/x-sh",
            "json" => "application/json",
            "yaml" or "yml" => "application/yaml",
            "toml" => "application/toml",
            "xml" => "application/xml",
            "svg" => "image/svg+xml",
            "png" => "image/png",
            "jpg" or "jpeg" => "image/jpeg",
            "gif" => "image/gif",
            "webp" => "image/webp",
            "pdf" => "application/pdf",
            _ => TryDecodeUtf8(content, out _) ? "text/plain" : "application/octet-stream",
        };
    }
}
