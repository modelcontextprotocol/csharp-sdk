using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json.Nodes;

namespace ModelContextProtocol.Extensions.Skills;

/// <summary>
/// Represents an authored skill and the ordinary resources serving its files.
/// </summary>
/// <remarks>
/// <para>
/// The specification requires a skill's manifest to carry the SHA-256 digest and size of every file, and a host
/// refuses content whose bytes do not match. Building a skill through <see cref="Create(IEnumerable{McpServerSkillFile}, McpServerSkillPackageCreateOptions)"/>
/// or <see cref="CreateFromDirectory(string, McpServerSkillPackageCreateOptions)"/> computes the manifest from the same bytes the
/// resources serve. Applications must keep later entry mutations consistent with those resources.
/// </para>
/// <para>
/// Factories without explicit frontmatter read the frontmatter with the SDK's built-in reader and reject unsupported
/// or malformed input rather than publish guessed metadata. The reader handles a bounded subset of YAML: block
/// mappings and sequences, plain, quoted and block scalars, single-line flow collections, and comments.
/// Explicitly supplied frontmatter supports YAML the reader does not understand; see
/// <see cref="McpServerSkillPackageCreateOptions.Frontmatter"/> for what they check and what remains
/// the caller's responsibility.
/// </para>
/// <para>
/// Factories capture their options settings, but may retain supplied frontmatter, entry metadata, and local
/// metadata references. Applications own mutations to those shared objects.
/// </para>
/// <para>
/// Authoring has no registration side effects. Register both components with
/// <see cref="McpSkillsBuilderExtensions.WithSkillPackages"/>, or register <see cref="Skill"/> and
/// <see cref="Resources"/> independently. Application overrides own their resulting manifest/content agreement.
/// </para>
/// </remarks>
[Experimental(Experimentals.Skills_DiagnosticId, UrlFormat = Experimentals.Skills_Url)]
public sealed class McpServerSkillPackage
{
    private McpServerSkillPackage(McpServerSkill skill, McpServerResource[] resources)
    {
        Skill = skill;
        Resources = Array.AsReadOnly(resources);
    }

    /// <summary>Gets the authored skill primitive.</summary>
    public McpServerSkill Skill { get; }

    /// <summary>Gets the read-only collection of resources serving the authored files.</summary>
    /// <remarks>
    /// The resource implementations serve the same owned bytes used to compute the skill's manifest.
    /// Textual MIME types use <see cref="TextResourceContents"/> when the bytes decode losslessly as UTF-8;
    /// other content uses <see cref="BlobResourceContents"/>.
    /// Registering or removing these resources is independent of registering or removing <see cref="Skill"/>.
    /// </remarks>
    public IReadOnlyList<McpServerResource> Resources { get; }

    /// <summary>
    /// Creates a skill from its files, computing the manifest from the bytes that its resources serve.
    /// </summary>
    /// <param name="files">The skill's files. Exactly one must have the path <c>SKILL.md</c>.</param>
    /// <param name="options">Optional URI, frontmatter, and metadata customization.</param>
    /// <returns>The authored skill package.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="files"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="files"/> omits <c>SKILL.md</c> or contains a duplicate or unsafe path; or the
    /// <c>SKILL.md</c> frontmatter cannot be read (it is malformed or uses
    /// YAML the reader does not support) or is missing a required field; or the options do not match the files.
    /// </exception>
    /// <remarks>
    /// By default, the SDK reads frontmatter from <c>SKILL.md</c> and derives the URI from its name.
    /// Use <see cref="McpServerSkillPackageCreateOptions.Frontmatter"/> for YAML the reader does not support.
    /// File bytes are owned by the package. Keep entries consistent with the authored resources when making changes.
    /// </remarks>
    public static McpServerSkillPackage Create(IEnumerable<McpServerSkillFile> files, McpServerSkillPackageCreateOptions? options = null)
    {
#if NET
        ArgumentNullException.ThrowIfNull(files);
#else
        if (files is null) throw new ArgumentNullException(nameof(files));
#endif

        return CreateCore(files, nameof(files), options?.Clone());
    }

    /// <summary>
    /// Creates a skill from a directory's selected files, snapshotting their bytes and computing the manifest.
    /// </summary>
    /// <param name="directoryPath">The skill's root directory. It must contain a <c>SKILL.md</c>.</param>
    /// <param name="options">Optional URI, frontmatter, metadata, and hidden/system-file selection.</param>
    /// <returns>The authored skill package.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="directoryPath"/> is <see langword="null"/>.</exception>
    /// <exception cref="DirectoryNotFoundException"><paramref name="directoryPath"/> does not exist.</exception>
    /// <exception cref="IOException">A file cannot be read or changes size while it is being snapshotted.</exception>
    /// <exception cref="ArgumentException">
    /// The directory's contents do not form a valid skill (see <see cref="Create(IEnumerable{McpServerSkillFile}, McpServerSkillPackageCreateOptions)"/>),
    /// or the directory contains a symbolic link or other reparse point.
    /// </exception>
    /// <remarks>
    /// <para>
    /// Files are read once, when this method is called. Changes on disk afterwards are not reflected in the
    /// manifest or the served content.
    /// </para>
    /// <para>
    /// Every selected file is eagerly read and retained. This convenience API has no file-count, total-byte, or
    /// deadline budget and is intended for application-controlled directories. For bounded or untrusted inputs,
    /// perform bounded reads in application code and pass the selected files to <see cref="Create(IEnumerable{McpServerSkillFile}, McpServerSkillPackageCreateOptions)"/>.
    /// The specification recommends that servers stay within 512 files and 16 MiB per skill for portability,
    /// but the SDK does not enforce those thresholds as maximums.
    /// </para>
    /// <para>
    /// Dot-prefixed, hidden, and system files and directories are excluded before reading or descending into them.
    /// Set <see cref="McpServerSkillPackageCreateOptions.IncludeHiddenAndSystemFiles"/> to include them explicitly, or
    /// use <see cref="Create(IEnumerable{McpServerSkillFile}, McpServerSkillPackageCreateOptions)"/> with an explicit file selection.
    /// </para>
    /// <para>
    /// Links are not followed. A symbolic link inside the directory could point outside it and publish a file
    /// under a URI that appears to belong to the skill, so encountering one is an error. Replace the link with a
    /// regular file or directory, or build the skill with <see cref="Create(IEnumerable{McpServerSkillFile}, McpServerSkillPackageCreateOptions)"/> and
    /// explicit files.
    /// </para>
    /// </remarks>
    public static McpServerSkillPackage CreateFromDirectory(string directoryPath, McpServerSkillPackageCreateOptions? options = null)
    {
#if NET
        ArgumentNullException.ThrowIfNull(directoryPath);
#else
        if (directoryPath is null) throw new ArgumentNullException(nameof(directoryPath));
#endif

        var captured = options?.Clone();
        return CreateCore(ReadDirectory(directoryPath, captured?.IncludeHiddenAndSystemFiles ?? false), nameof(directoryPath), captured);
    }

    private const string DefaultUriPrefix = "skill://";

    /// <summary>
    /// Authors packages only from the immediate child directories containing <c>SKILL.md</c>.
    /// </summary>
    /// <param name="directoryPath">The parent directory whose immediate children are skill directories.</param>
    /// <param name="options">Optional common URI prefix, file selection, and metadata.</param>
    /// <returns>A fully authored, read-only list of packages in ordinal child-directory order.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="directoryPath"/> is null.</exception>
    /// <exception cref="DirectoryNotFoundException"><paramref name="directoryPath"/> does not exist.</exception>
    /// <exception cref="ArgumentException">No skills are found, a skill is invalid, or package manifests conflict.</exception>
    /// <remarks>
    /// Discovery does not search deeper directories for additional skills. Each selected directory's files are
    /// collected recursively, but a nested <c>SKILL.md</c> is a file of that skill, not an additional registration.
    /// Shared file URIs must have agreeing digest and size across the complete batch, including duplicate skill
    /// identities. The factories do not register services, read files again to compare packages, or mutate options.
    /// </remarks>
    public static IReadOnlyList<McpServerSkillPackage> CreateFromSubdirectories(
        string directoryPath, McpServerSkillPackageDiscoveryOptions? options = null)
    {
        if (directoryPath is null) throw new ArgumentNullException(nameof(directoryPath));
        string uriPrefix = options is null ? DefaultUriPrefix : options.UriPrefix;
        if (string.IsNullOrEmpty(uriPrefix))
        {
            throw new ArgumentException("The UriPrefix option must not be null or empty.", nameof(options));
        }

        if (!uriPrefix.EndsWith("/", StringComparison.Ordinal))
        {
            uriPrefix += "/";
        }

        var captured = new McpServerSkillPackageCreateOptions
        {
            IncludeHiddenAndSystemFiles = options?.IncludeHiddenAndSystemFiles ?? false,
            Metadata = options?.Metadata,
            Meta = options?.Meta,
        };
        string fullDirectory = Path.GetFullPath(directoryPath);
        if (!Directory.Exists(fullDirectory))
        {
            throw new DirectoryNotFoundException($"The skills directory '{fullDirectory}' does not exist.");
        }

        var directories = new List<string>(Directory.EnumerateDirectories(fullDirectory));
        directories.Sort(StringComparer.Ordinal);
        var packages = new List<McpServerSkillPackage>();
        foreach (string directory in directories)
        {
            if (!captured.IncludeHiddenAndSystemFiles &&
                Path.GetFileName(directory).StartsWith(".", StringComparison.Ordinal))
            {
                continue;
            }

            var attributes = File.GetAttributes(directory);
            if (!captured.IncludeHiddenAndSystemFiles &&
                (attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0)
            {
                continue;
            }

            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new ArgumentException(
                    $"'{directory}' is a symbolic link or other reparse point. Links are not followed when loading skills, " +
                    "because a link can point outside the skills directory. Replace it with a regular directory.",
                    nameof(directoryPath));
            }

            if (File.Exists(Path.Combine(directory, SkillsProtocol.SkillFileName)))
            {
                packages.Add(CreateCore(ReadDirectory(directory, captured.IncludeHiddenAndSystemFiles),
                    nameof(directoryPath), captured, uriPrefix));
            }
        }

        if (packages.Count == 0)
        {
            throw new ArgumentException(
                $"No skill directories were found under '{fullDirectory}'. Each skill must be an immediate subdirectory containing a {SkillsProtocol.SkillFileName}.",
                nameof(directoryPath));
        }

        ValidatePackages(packages, nameof(directoryPath));
        return packages.AsReadOnly();
    }

    internal static void ValidatePackages(IReadOnlyList<McpServerSkillPackage> packages, string paramName)
    {
        var files = new Dictionary<Uri, SkillResource>();
        foreach (var package in packages)
        {
            if (package is null)
            {
                throw new ArgumentException("The packages must not contain null entries.", paramName);
            }

            var entry = package.Skill.ProtocolSkill ??
                throw new ArgumentException("A package must provide a complete skill entry.", paramName);
            SkillValidation.Validate(entry, paramName);
            if (!SkillUriComparer.Instance.Equals(entry.Uri, package.Skill.Id))
            {
                throw new ArgumentException(
                    $"Package skill '{package.Skill.Id}' has an entry for '{entry.Uri}'.", paramName);
            }

            AddSharedFiles(files, entry, paramName);
        }
    }

    private static void AddSharedFiles(Dictionary<Uri, SkillResource> files, Skill entry, string paramName)
    {
        if (entry.Resources.Entries is not { } manifest)
        {
            return;
        }

        foreach (var file in manifest)
        {
            var key = new Uri(file.Uri, UriKind.Absolute);
            if (files.TryGetValue(key, out var existing) &&
                (existing.Digest != file.Digest || existing.Size != file.Size))
            {
                throw new ArgumentException(
                    $"The file '{file.Uri}' conflicts with the equivalent URI '{existing.Uri}'. Shared files must have identical digest and size.",
                    paramName);
            }

            files[key] = file;
        }
    }

    private static McpServerSkillPackage CreateCore(
        IEnumerable<McpServerSkillFile> files, string filesParamName, McpServerSkillPackageCreateOptions? options = null, string uriPrefix = DefaultUriPrefix)
    {
        string? uri = options?.Uri;
        JsonObject? frontmatter = options?.Frontmatter;
        string entryParamName = frontmatter is null ? filesParamName : nameof(options);
        string uriParamName = options?.Uri is not null || uriPrefix != DefaultUriPrefix ? nameof(options) : filesParamName;

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

        // Read frontmatter from SKILL.md (always first after sorting). UTF-8, boundaries, detected format errors,
        // and equality for completely parsed blocks are checked even when metadata was supplied. An unsupported
        // construct can stop the reader before later content is examined. In that case the caller owns parsing
        // and validating the entire frontmatter and supplying matching JSON; this is not full YAML validation.
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
                $"The frontmatter of {SkillsProtocol.SkillFileName} uses YAML that the frontmatter reader does not support: {e.Message} " +
                "Supply a matching JsonObject through McpServerSkillPackageCreateOptions.Frontmatter.",
                filesParamName);
        }
        catch (SkillFrontmatter.UnsupportedYamlException)
        {
            // The application must already have validated the entire frontmatter and supplied matching JSON.
            // Reaching an unsupported construct establishes neither whole-document validity nor equality.
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
                "and refuse the skill on any difference. Fix the mismatch, or omit the Frontmatter option to have it read from the file. " +
                $"From the file: {fileFrontmatter.ToJsonString()} Supplied: {frontmatter.ToJsonString()}",
                nameof(options));
        }

        if (uri is null)
        {
            string? name = new Skill { Uri = string.Empty, Frontmatter = frontmatter, Resources = SkillResources.Dynamic }.Name;
            if (string.IsNullOrEmpty(name))
            {
                throw new ArgumentException(
                    $"The frontmatter of {SkillsProtocol.SkillFileName} must declare a string 'name' for the skill's URI to be derived from it.",
                    entryParamName);
            }

            var uriName = uriPrefix.EndsWith("://", StringComparison.Ordinal) ? name : Uri.EscapeDataString(name);
            uri = uriPrefix + uriName + "/" + SkillsProtocol.SkillFileName;
        }

        string root = SkillValidation.GetSkillRoot(uri, uriParamName);

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
            Meta = options?.Meta,
        };

        var primitive = McpServerSkill.Create(skill, options?.Metadata, entryParamName, uriParamName);

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

        return new McpServerSkillPackage(primitive, resources);
    }

    private static List<McpServerSkillFile> ReadDirectory(string directoryPath, bool includeHiddenAndSystemFiles)
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
        CollectFiles(fullDirectory, fullDirectory, files, includeHiddenAndSystemFiles);
        return files;
    }

    /// <summary>
    /// Walks a skill directory without following links. A symbolic link (or any other reparse point) can point
    /// outside the skill directory, and a file reached through one would be published under a URI that looks like
    /// it lives inside the skill. Rather than try to decide which link targets are acceptable, links are rejected.
    /// </summary>
    private static void CollectFiles(
        string root, string directory, List<McpServerSkillFile> files, bool includeHiddenAndSystemFiles)
    {
        foreach (string entry in Directory.EnumerateFileSystemEntries(directory))
        {
            if (!includeHiddenAndSystemFiles &&
                Path.GetFileName(entry).StartsWith(".", StringComparison.Ordinal))
            {
                continue;
            }
            FileAttributes attributes = File.GetAttributes(entry);
            if (!includeHiddenAndSystemFiles &&
                (attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0)
            {
                continue;
            }
            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new ArgumentException(
                    $"'{entry}' is a symbolic link or other reparse point. Links are not followed when loading a skill directory, " +
                    $"because a link can point outside the skill. Replace it with a regular file or directory, or build the skill " +
                    $"with {nameof(McpServerSkillPackage)}.{nameof(Create)} and explicit files.",
                    "directoryPath");
            }

            if ((attributes & FileAttributes.Directory) != 0)
            {
                CollectFiles(root, entry, files, includeHiddenAndSystemFiles);
                continue;
            }

            long length = new FileInfo(entry).Length;

            string relativePath = entry.Substring(root.Length).Replace(Path.DirectorySeparatorChar, '/');
            if (Path.AltDirectorySeparatorChar != Path.DirectorySeparatorChar)
            {
                relativePath = relativePath.Replace(Path.AltDirectorySeparatorChar, '/');
            }

            files.Add(new McpServerSkillFile
            {
                Path = relativePath,
                Content = ReadExactly(entry, length),
            });
        }
    }

    /// <summary>
    /// Reads the observed length and fails if the file grows or shrinks while it is being snapshotted.
    /// </summary>
    private static byte[] ReadExactly(string path, long expectedLength)
    {
        var buffer = new byte[expectedLength];
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        int total = 0;
        while (total < buffer.Length)
        {
            int read = stream.Read(buffer, total, buffer.Length - total);
            if (read == 0)
            {
                break;
            }

            total += read;
        }

        if (total != buffer.Length || stream.ReadByte() != -1)
        {
            throw new IOException($"'{path}' changed size while it was being read.");
        }

        return buffer;
    }

    /// <summary>
    /// Each segment of a normalized relative path is escaped with <see cref="Uri.EscapeDataString(string)"/> so that
    /// characters with URI syntax (such as <c>{</c>, <c>?</c>, <c>#</c>, or a space) in a file name stay literal.
    /// Without this, a file named <c>{name}.md</c> would register as a resource template rather than a concrete resource.
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
#if NET
            text = s_strictUtf8.GetString(bytes);
#else
            text = s_strictUtf8.GetString(bytes.ToArray());
#endif
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
