using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.Text;
using System.Text.Json.Nodes;

namespace ModelContextProtocol.Extensions.Skills;

/// <summary>
/// Represents a server-side skill, retaining its identity, local metadata, protocol entry, and any authored file resources.
/// </summary>
/// <remarks>
/// <para>
/// The specification requires a skill's manifest to carry the SHA-256 digest and size of every file, and a host
/// refuses content whose bytes do not match. Building a skill through <see cref="Create(IEnumerable{McpServerSkillFile}, McpServerSkillCreateOptions)"/>
/// or <see cref="CreateFromDirectory(string, McpServerSkillCreateOptions)"/> (or their overloads) computes the manifest from the same bytes the
/// resources serve, so the two cannot disagree.
/// </para>
/// <para>
/// Overloads without explicit metadata read the frontmatter with the SDK's built-in reader and reject unsupported
/// or malformed input rather than publish guessed metadata. The reader handles a bounded subset of YAML: block
/// mappings and sequences, plain, quoted and block scalars, single-line flow collections, and comments. Overloads
/// accepting a <see cref="JsonObject"/> exist for YAML the reader does not support; see
/// <see cref="Create(string, JsonObject, IEnumerable{McpServerSkillFile}, McpServerSkillCreateOptions)"/> for what they check and what remains
/// the caller's responsibility.
/// </para>
/// <para>
/// Register skills with <see cref="McpSkillsBuilderExtensions.WithSkills(IMcpServerBuilder, IEnumerable{McpServerSkill}, Action{McpSkillsOptions})"/>,
/// which registers both the primitives and the file resources, or point
/// <see cref="McpSkillsBuilderExtensions.WithSkillsFromDirectory(IMcpServerBuilder, string, string, Action{McpSkillsOptions})"/>
/// at a directory of skills.
/// </para>
/// </remarks>
public sealed class McpServerSkill : IMcpServerPrimitive
{
    private readonly Skill _entry;

    /// <summary>
    /// Creates a server-side primitive from an existing protocol entry, without creating file resources.
    /// </summary>
    /// <param name="protocolSkill">The structurally valid entry to snapshot.</param>
    /// <param name="options">Optional creation options, including server-local metadata.</param>
    /// <remarks>
    /// The application must serve the entry's files separately and keep their content and frontmatter in agreement
    /// with this entry. No file is read to validate the entry. Prefer <c>Create</c> or <c>CreateFromDirectory</c>
    /// when the SDK should compute the manifest from the served bytes.
    /// </remarks>
    public McpServerSkill(Skill protocolSkill, McpServerSkillCreateOptions? options = null)
        : this(protocolSkill, [], options?.Metadata)
    {
    }

    private McpServerSkill(Skill protocolSkill, IReadOnlyList<McpServerResource> resources, IReadOnlyList<object>? metadata = null)
    {
        SkillValidation.Validate(protocolSkill, nameof(protocolSkill));
        _entry = SkillValidation.Snapshot(protocolSkill);
        SkillValidation.Validate(_entry, nameof(protocolSkill));
        Resources = Array.AsReadOnly(resources.ToArray());
        Metadata = metadata is null ? [] : Array.AsReadOnly(metadata.ToArray());
    }

    /// <summary>Gets the skill's immutable identity: the exact URI text of its <c>SKILL.md</c>.</summary>
    public string Id => _entry.Uri;

    /// <summary>Gets the server-local metadata associated with this primitive.</summary>
    /// <remarks>
    /// These objects are not included in the protocol entry. They are available to application handlers through
    /// <see cref="RequestContext{TParams}.MatchedPrimitive"/>; the SDK does not interpret them as authorization policies.
    /// </remarks>
    public IReadOnlyList<object> Metadata { get; }

    /// <summary>
    /// Gets a copy of the skill's entry, as returned by <c>skills/list</c> and <c>skills/get</c>.
    /// </summary>
    /// <remarks>
    /// Each access returns a new copy. The manifest was computed from the same bytes <see cref="Resources"/> serve;
    /// frontmatter was read or supplied at creation. Changes to a returned copy affect neither the skill nor
    /// what <c>WithSkills</c> registers.
    /// </remarks>
    public Skill ProtocolSkill => SkillValidation.Snapshot(_entry);

    internal Skill Entry => _entry;

    /// <summary>
    /// Gets the resources serving the skill's files, one per file, each addressable at the URI its manifest entry names.
    /// </summary>
    public IReadOnlyList<McpServerResource> Resources { get; }

    /// <summary>
    /// Creates a skill from its files, reading the frontmatter from <c>SKILL.md</c> and deriving the skill's URI
    /// from the frontmatter's <c>name</c> as <c>skill://{name}/SKILL.md</c>.
    /// </summary>
    /// <param name="files">The skill's files. Exactly one must have the path <c>SKILL.md</c>.</param>
    /// <param name="options">Optional creation options, including server-local metadata.</param>
    /// <returns>The skill.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="files"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="files"/> omits <c>SKILL.md</c>, contains a duplicate or unsafe path, or exceeds the
    /// specification's per-skill limits; or the <c>SKILL.md</c> frontmatter cannot be read (it is malformed or uses
    /// YAML the reader does not support) or is missing a required field.
    /// </exception>
    public static McpServerSkill Create(IEnumerable<McpServerSkillFile> files, McpServerSkillCreateOptions? options = null)
    {
#if NET
        ArgumentNullException.ThrowIfNull(files);
#else
        if (files is null) throw new ArgumentNullException(nameof(files));
#endif

        return CreateCore(uri: null, uriPrefix: DefaultUriPrefix, frontmatter: null, files, nameof(files), options);
    }

    /// <summary>
    /// Creates a skill from its files, reading the frontmatter from <c>SKILL.md</c>.
    /// </summary>
    /// <param name="uri">
    /// The resource URI of the skill's <c>SKILL.md</c>, for example <c>skill://git-workflow/SKILL.md</c>. The path
    /// segment preceding <c>/SKILL.md</c>, decoded once, must equal the skill's name.
    /// </param>
    /// <param name="files">The skill's files. Exactly one must have the path <c>SKILL.md</c>.</param>
    /// <param name="options">Optional creation options, including server-local metadata.</param>
    /// <returns>The skill.</returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="uri"/> does not end in <c>/SKILL.md</c>; <paramref name="files"/> omits <c>SKILL.md</c>,
    /// contains a duplicate or unsafe path, or exceeds the specification's per-skill limits; or the <c>SKILL.md</c>
    /// frontmatter cannot be read (it is malformed or uses YAML the reader does not support), is missing a required
    /// field, or has a name that does not match <paramref name="uri"/>.
    /// </exception>
    public static McpServerSkill Create(string uri, IEnumerable<McpServerSkillFile> files, McpServerSkillCreateOptions? options = null)
    {
#if NET
        ArgumentNullException.ThrowIfNull(uri);
        ArgumentNullException.ThrowIfNull(files);
#else
        if (uri is null) throw new ArgumentNullException(nameof(uri));
        if (files is null) throw new ArgumentNullException(nameof(files));
#endif

        return CreateCore(uri, uriPrefix: null, frontmatter: null, files, nameof(files), options);
    }

    /// <summary>
    /// Creates a skill from its files with explicitly supplied frontmatter.
    /// </summary>
    /// <param name="uri">
    /// The resource URI of the skill's <c>SKILL.md</c>, for example <c>skill://git-workflow/SKILL.md</c>. The path
    /// segment preceding <c>/SKILL.md</c>, decoded once, must equal the skill's name.
    /// </param>
    /// <param name="frontmatter">
    /// The <c>SKILL.md</c> YAML frontmatter rendered as a JSON object. It must contain string <c>name</c> and
    /// <c>description</c> fields and reproduce the authored frontmatter exactly.
    /// </param>
    /// <param name="files">The skill's files. Exactly one must have the path <c>SKILL.md</c>.</param>
    /// <param name="options">Optional creation options, including server-local metadata.</param>
    /// <returns>The skill.</returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="uri"/> does not end in <c>/SKILL.md</c>; <paramref name="frontmatter"/> is missing a required
    /// field, has a name that does not match <paramref name="uri"/>, or differs from frontmatter the reader can
    /// read completely; or <paramref name="files"/> omits <c>SKILL.md</c>, contains a duplicate or unsafe path,
    /// exceeds the specification's per-skill limits, or has invalid UTF-8, missing frontmatter boundaries,
    /// or a format error detected by the reader.
    /// </exception>
    /// <remarks>
    /// <para>
    /// Prefer <see cref="Create(string, IEnumerable{McpServerSkillFile}, McpServerSkillCreateOptions)"/>, which reads the frontmatter from the
    /// file. This overload still checks UTF-8, the frontmatter boundaries, and the entry's structure, computes the
    /// manifest from the served bytes, and rejects malformed YAML the reader detects. When the reader reads the
    /// complete block, the supplied object must match it exactly.
    /// </para>
    /// <para>
    /// When the reader stops at an unsupported construct, it does not examine the rest of the block, so acceptance
    /// here proves neither that the YAML is valid nor that it matches <paramref name="frontmatter"/>. The caller
    /// must have parsed the entire frontmatter with a full YAML parser and supplied the identical JSON.
    /// </para>
    /// </remarks>
    public static McpServerSkill Create(string uri, JsonObject frontmatter, IEnumerable<McpServerSkillFile> files, McpServerSkillCreateOptions? options = null)
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

        return CreateCore(uri, uriPrefix: null, frontmatter, files, nameof(files), options);
    }

    /// <summary>
    /// Creates a skill from the visible, non-system files in a directory, recursively, reading the frontmatter from its <c>SKILL.md</c>
    /// and deriving the skill's URI from the frontmatter's <c>name</c> as <c>skill://{name}/SKILL.md</c>.
    /// </summary>
    /// <param name="directoryPath">The skill's root directory. It must contain a <c>SKILL.md</c>.</param>
    /// <param name="options">Optional creation options, including server-local metadata.</param>
    /// <returns>The skill.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="directoryPath"/> is <see langword="null"/>.</exception>
    /// <exception cref="DirectoryNotFoundException"><paramref name="directoryPath"/> does not exist.</exception>
    /// <exception cref="ArgumentException">
    /// The directory's contents do not form a valid skill (see <see cref="Create(IEnumerable{McpServerSkillFile}, McpServerSkillCreateOptions)"/>),
    /// or the directory contains a symbolic link or other reparse point.
    /// </exception>
    /// <remarks>
    /// <para>
    /// Files are read once, when this method is called. Changes on disk afterwards are not reflected in the
    /// manifest or the served content.
    /// </para>
    /// <para>
    /// Dot-prefixed, hidden, and system files and directories are excluded before reading or descending into them.
    /// Use <see cref="CreateFromDirectory(string, bool, McpServerSkillCreateOptions)"/> to include them explicitly, or
    /// <see cref="Create(IEnumerable{McpServerSkillFile}, McpServerSkillCreateOptions)"/> with an explicit file selection.
    /// </para>
    /// <para>
    /// Links are not followed. A symbolic link inside the directory could point outside it and publish a file
    /// under a URI that appears to belong to the skill, so encountering one is an error. Replace the link with a
    /// regular file or directory, or build the skill with <see cref="Create(IEnumerable{McpServerSkillFile}, McpServerSkillCreateOptions)"/> and
    /// explicit files.
    /// </para>
    /// </remarks>
    public static McpServerSkill CreateFromDirectory(string directoryPath, McpServerSkillCreateOptions? options = null) =>
        CreateFromDirectory(directoryPath, includeHiddenAndSystemFiles: false, options);

    /// <summary>
    /// Creates a skill from a directory with an explicit hidden/system-file policy.
    /// </summary>
    /// <param name="directoryPath">The skill's root directory, containing <c>SKILL.md</c>.</param>
    /// <param name="includeHiddenAndSystemFiles">Whether to include dot-prefixed, hidden, and system entries.</param>
    /// <param name="options">Optional creation options, including server-local metadata.</param>
    /// <returns>The skill with its manifest computed from the selected files.</returns>
    /// <remarks>Included symbolic links remain errors. See <see cref="CreateFromDirectory(string, McpServerSkillCreateOptions)"/>.</remarks>
    public static McpServerSkill CreateFromDirectory(string directoryPath, bool includeHiddenAndSystemFiles, McpServerSkillCreateOptions? options = null)
    {
#if NET
        ArgumentNullException.ThrowIfNull(directoryPath);
#else
        if (directoryPath is null) throw new ArgumentNullException(nameof(directoryPath));
#endif

        return CreateCore(uri: null, uriPrefix: DefaultUriPrefix, frontmatter: null,
            ReadDirectory(directoryPath, includeHiddenAndSystemFiles), nameof(directoryPath), options);
    }

    /// <summary>
    /// Creates a skill from the visible, non-system files in a directory, recursively, reading the frontmatter from its <c>SKILL.md</c>.
    /// </summary>
    /// <param name="uri">
    /// The resource URI of the skill's <c>SKILL.md</c>, for example <c>skill://git-workflow/SKILL.md</c>. The path
    /// segment preceding <c>/SKILL.md</c>, decoded once, must equal the skill's name.
    /// </param>
    /// <param name="directoryPath">The skill's root directory. It must contain a <c>SKILL.md</c>.</param>
    /// <param name="options">Optional creation options, including server-local metadata.</param>
    /// <returns>The skill.</returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    /// <exception cref="DirectoryNotFoundException"><paramref name="directoryPath"/> does not exist.</exception>
    /// <exception cref="ArgumentException">
    /// The directory's contents do not form a valid skill (see <see cref="Create(string, IEnumerable{McpServerSkillFile}, McpServerSkillCreateOptions)"/>),
    /// or the directory contains a symbolic link or other reparse point.
    /// </exception>
    /// <remarks>
    /// See <see cref="CreateFromDirectory(string, McpServerSkillCreateOptions)"/> for how files and links are handled.
    /// </remarks>
    public static McpServerSkill CreateFromDirectory(string uri, string directoryPath, McpServerSkillCreateOptions? options = null) =>
        CreateFromDirectory(uri, directoryPath, includeHiddenAndSystemFiles: false, options);

    /// <summary>
    /// Creates a skill with an explicit URI and hidden/system-file policy.
    /// </summary>
    /// <param name="uri">The resource URI of the skill's <c>SKILL.md</c>.</param>
    /// <param name="directoryPath">The skill's root directory.</param>
    /// <param name="includeHiddenAndSystemFiles">Whether to include dot-prefixed, hidden, and system entries.</param>
    /// <param name="options">Optional creation options, including server-local metadata.</param>
    /// <returns>The skill.</returns>
    /// <remarks>Included symbolic links remain errors. See <see cref="CreateFromDirectory(string, McpServerSkillCreateOptions)"/>.</remarks>
    public static McpServerSkill CreateFromDirectory(string uri, string directoryPath, bool includeHiddenAndSystemFiles, McpServerSkillCreateOptions? options = null)
    {
#if NET
        ArgumentNullException.ThrowIfNull(uri);
        ArgumentNullException.ThrowIfNull(directoryPath);
#else
        if (uri is null) throw new ArgumentNullException(nameof(uri));
        if (directoryPath is null) throw new ArgumentNullException(nameof(directoryPath));
#endif

        return CreateCore(uri, uriPrefix: null, frontmatter: null,
            ReadDirectory(directoryPath, includeHiddenAndSystemFiles), nameof(directoryPath), options);
    }

    /// <summary>
    /// Creates a skill from the visible, non-system files in a directory, recursively, with explicitly supplied frontmatter.
    /// </summary>
    /// <param name="uri">
    /// The resource URI of the skill's <c>SKILL.md</c>, for example <c>skill://git-workflow/SKILL.md</c>. The path
    /// segment preceding <c>/SKILL.md</c>, decoded once, must equal the skill's name.
    /// </param>
    /// <param name="frontmatter">
    /// The <c>SKILL.md</c> YAML frontmatter rendered as a JSON object. It must contain string <c>name</c> and
    /// <c>description</c> fields and reproduce the authored frontmatter exactly.
    /// </param>
    /// <param name="directoryPath">The skill's root directory. It must contain a <c>SKILL.md</c>.</param>
    /// <param name="options">Optional creation options, including server-local metadata.</param>
    /// <returns>The skill.</returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    /// <exception cref="DirectoryNotFoundException"><paramref name="directoryPath"/> does not exist.</exception>
    /// <exception cref="ArgumentException">
    /// The directory's contents do not form a valid skill (see <see cref="Create(string, JsonObject, IEnumerable{McpServerSkillFile}, McpServerSkillCreateOptions)"/>),
    /// or the directory contains a symbolic link or other reparse point.
    /// </exception>
    /// <remarks>
    /// Prefer <see cref="CreateFromDirectory(string, string, McpServerSkillCreateOptions)"/>. The supplied frontmatter is checked, and remains
    /// the caller's responsibility, as described for <see cref="Create(string, JsonObject, IEnumerable{McpServerSkillFile}, McpServerSkillCreateOptions)"/>.
    /// See <see cref="CreateFromDirectory(string, McpServerSkillCreateOptions)"/> for how files and links are handled.
    /// </remarks>
    public static McpServerSkill CreateFromDirectory(string uri, JsonObject frontmatter, string directoryPath, McpServerSkillCreateOptions? options = null) =>
        CreateFromDirectory(uri, frontmatter, directoryPath, includeHiddenAndSystemFiles: false, options);

    /// <summary>
    /// Creates a skill with explicit frontmatter and a hidden/system-file policy.
    /// </summary>
    /// <param name="uri">The resource URI of the skill's <c>SKILL.md</c>.</param>
    /// <param name="frontmatter">The fully validated JSON frontmatter, matching the file.</param>
    /// <param name="directoryPath">The skill's root directory.</param>
    /// <param name="includeHiddenAndSystemFiles">Whether to include dot-prefixed, hidden, and system entries.</param>
    /// <param name="options">Optional creation options, including server-local metadata.</param>
    /// <returns>The skill.</returns>
    /// <remarks>
    /// See <see cref="Create(string, JsonObject, IEnumerable{McpServerSkillFile}, McpServerSkillCreateOptions)"/> for caller responsibilities.
    /// Included symbolic links remain errors.
    /// </remarks>
    public static McpServerSkill CreateFromDirectory(
        string uri, JsonObject frontmatter, string directoryPath, bool includeHiddenAndSystemFiles, McpServerSkillCreateOptions? options = null)
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

        return CreateCore(uri, uriPrefix: null, frontmatter,
            ReadDirectory(directoryPath, includeHiddenAndSystemFiles), nameof(directoryPath), options);
    }

    private const string DefaultUriPrefix = "skill://";

    /// <summary>
    /// Creates a skill from a directory, deriving its URI as <c>{uriPrefix}{name}/SKILL.md</c>. Used by
    /// <c>WithSkillsFromDirectory</c>.
    /// </summary>
    internal static McpServerSkill CreateFromDirectory(
        string directoryPath, string uriPrefix, string paramName, bool includeHiddenAndSystemFiles = false) =>
        CreateCore(uri: null, uriPrefix, frontmatter: null, ReadDirectory(directoryPath, includeHiddenAndSystemFiles), paramName);

    private static McpServerSkill CreateCore(string? uri, string? uriPrefix, JsonObject? frontmatter, IEnumerable<McpServerSkillFile> files, string filesParamName, McpServerSkillCreateOptions? options = null)
    {
        // A violation found in the entry is reported against the frontmatter argument when the caller supplied
        // one, and against the files (or directory) it was read from otherwise.
        string entryParamName = frontmatter is null ? filesParamName : nameof(frontmatter);

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
                "Supply the frontmatter explicitly with the overload that takes a JsonObject.",
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

            var uriName = uriPrefix!.EndsWith("://", StringComparison.Ordinal) ? name : Uri.EscapeDataString(name);
            uri = uriPrefix + uriName + "/" + SkillsProtocol.SkillFileName;
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

        SkillValidation.Validate(skill, entryParamName);

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

        return new McpServerSkill(skill, resources, options?.Metadata);
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
        long totalSize = 0;
        CollectFiles(fullDirectory, fullDirectory, files, ref totalSize, includeHiddenAndSystemFiles);
        return files;
    }

    /// <summary>
    /// Walks a skill directory without following links. A symbolic link (or any other reparse point) can point
    /// outside the skill directory, and a file reached through one would be published under a URI that looks like
    /// it lives inside the skill. Rather than try to decide which link targets are acceptable, links are rejected.
    /// </summary>
    private static void CollectFiles(
        string root, string directory, List<McpServerSkillFile> files, ref long totalSize, bool includeHiddenAndSystemFiles)
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
                    $"with {nameof(McpServerSkill)}.{nameof(Create)} and explicit files.",
                    "directoryPath");
            }

            if ((attributes & FileAttributes.Directory) != 0)
            {
                CollectFiles(root, entry, files, ref totalSize, includeHiddenAndSystemFiles);
                continue;
            }

            // Apply the specification's per-skill limits before reading, so an oversized or overly broad directory
            // fails with the limit named rather than after allocating everything under it. The manifest built from
            // the bytes actually read is validated against the same limits afterwards.
            if (files.Count >= SkillsProtocol.MaxResourcesPerSkill)
            {
                throw new ArgumentException(
                    $"The skill directory '{root}' contains more than {SkillsProtocol.MaxResourcesPerSkill} files, the limit per skill.",
                    "directoryPath");
            }

            long length = new FileInfo(entry).Length;
            if (length > SkillsProtocol.MaxTotalSizeBytes - totalSize)
            {
                throw new ArgumentException(
                    $"The files under '{root}' total more than {SkillsProtocol.MaxTotalSizeBytes} bytes, the limit per skill.",
                    "directoryPath");
            }

            totalSize += length;

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
    /// Reads a file whose length was checked against the per-skill limits a moment ago, allocating only that
    /// length, and fails if the file turns out to be a different size, so a file that grows or shrinks between the
    /// check and the read can neither exceed the limit nor be served with a manifest that does not describe it.
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
            throw new ArgumentException($"'{path}' changed size while it was being read.", "directoryPath");
        }

        return buffer;
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
