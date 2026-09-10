using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;

namespace ModelContextProtocol.Extensions.Skills;

/// <summary>
/// Extension methods for <see cref="IMcpServerBuilder"/> to enable MCP Skills (SEP-2640) support.
/// </summary>
public static class McpSkillsBuilderExtensions
{
    /// <summary>
    /// Enables MCP Skills support for a fixed set of skills, serving both their entries and their files.
    /// </summary>
    /// <param name="builder">The server builder.</param>
    /// <param name="skills">The skills to serve.</param>
    /// <param name="configure">An optional callback that configures the extension's behavior.</param>
    /// <returns>The builder provided in <paramref name="builder"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> or <paramref name="skills"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// Two skills share a URI, or two skills list the same file URI with different content.
    /// </exception>
    /// <remarks>
    /// <para>
    /// This registers <c>skills/list</c> and <c>skills/get</c> backed by an <see cref="InMemoryMcpSkillCatalog"/>
    /// over the skills' entries, and registers each skill's <see cref="McpServerSkill.Resources"/> so the files
    /// are served through <c>resources/list</c> and <c>resources/read</c>.
    /// </para>
    /// <para>
    /// Nested skills may legitimately list the same file. A file URI shared by several skills is registered once,
    /// provided every skill lists it with the same digest and the identical URI text. URIs that differ only in the
    /// case of their scheme or authority are rejected, because the server's resource collection treats them as one.
    /// </para>
    /// <para>
    /// Every caller sees every skill. <c>skills/list</c> and <c>skills/get</c> are raw request handlers and do not
    /// pass through the request filters that guard the built-in resource methods, such as the ASP.NET Core
    /// authorization filters. When some callers must not see some skills, implement <see cref="IMcpSkillCatalog"/>
    /// and use <see cref="WithSkills(IMcpServerBuilder, IMcpSkillCatalog, Action{McpSkillsOptions})"/>, and guard
    /// the corresponding file resources separately.
    /// </para>
    /// </remarks>
    public static IMcpServerBuilder WithSkills(
        this IMcpServerBuilder builder,
        IEnumerable<McpServerSkill> skills,
        Action<McpSkillsOptions>? configure = null)
    {
#if NET
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(skills);
#else
        if (builder is null) throw new ArgumentNullException(nameof(builder));
        if (skills is null) throw new ArgumentNullException(nameof(skills));
#endif

        var entries = new List<Skill>();

        // The server's resource collection keys concrete resources by System.Uri equality, under which scheme and
        // authority are case-insensitive, and it registers silently. Detect shared files under those same
        // semantics, so two URIs the collection would merge are caught here rather than served as one another.
        var registeredFiles = new Dictionary<Uri, (string Uri, string Digest)>();
        foreach (var skill in skills)
        {
            if (skill is null)
            {
                throw new ArgumentException("The skills must not contain null entries.", nameof(skills));
            }

            entries.Add(skill.ProtocolSkill);

            var manifest = skill.ProtocolSkill.Resources.Resources!;
            for (int i = 0; i < manifest.Count; i++)
            {
                var entry = manifest[i];
                var key = new Uri(entry.Uri, UriKind.Absolute);
                if (registeredFiles.TryGetValue(key, out var existing))
                {
                    if (!string.Equals(existing.Uri, entry.Uri, StringComparison.Ordinal))
                    {
                        // Even with identical content the alias cannot be served: the collection registers one
                        // resource, whose contents carry the first URI, so a verified read of the second fails.
                        throw new ArgumentException(
                            $"The file '{entry.Uri}' is equivalent to '{existing.Uri}', listed by another skill. Resource URIs are compared " +
                            "case-insensitively in their scheme and authority, so the two would be served as one. Use identical URIs for a shared file.",
                            nameof(skills));
                    }

                    if (!string.Equals(existing.Digest, entry.Digest, StringComparison.Ordinal))
                    {
                        throw new ArgumentException(
                            $"The file '{entry.Uri}' is listed by more than one skill with different content.",
                            nameof(skills));
                    }

                    continue;
                }

                registeredFiles.Add(key, (entry.Uri, entry.Digest));
                builder.Services.AddSingleton(skill.Resources[i]);
            }
        }

        return WithSkills(builder, new InMemoryMcpSkillCatalog(entries), configure);
    }

    /// <summary>
    /// Enables MCP Skills support for every skill directory found directly under <paramref name="directoryPath"/>,
    /// serving both their entries and their files.
    /// </summary>
    /// <param name="builder">The server builder.</param>
    /// <param name="directoryPath">
    /// A directory whose immediate subdirectories are skills. Each subdirectory containing a <c>SKILL.md</c> becomes
    /// one skill; subdirectories without one are ignored.
    /// </param>
    /// <param name="uriPrefix">
    /// The prefix of each skill's URI. A skill's <c>SKILL.md</c> is published at <c>{uriPrefix}{name}/SKILL.md</c>,
    /// where <c>name</c> is the skill's frontmatter name. Defaults to <c>skill://</c>; use a longer prefix such as
    /// <c>skill://acme/billing/</c> to place the skills under an organizational path.
    /// </param>
    /// <param name="configure">An optional callback that configures the extension's behavior.</param>
    /// <returns>The builder provided in <paramref name="builder"/>.</returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    /// <exception cref="DirectoryNotFoundException"><paramref name="directoryPath"/> does not exist.</exception>
    /// <exception cref="ArgumentException">
    /// No subdirectory contains a <c>SKILL.md</c>, a skill directory is invalid (see
    /// <see cref="McpServerSkill.CreateFromDirectory(string)"/>), or two skills declare the same name.
    /// </exception>
    /// <remarks>
    /// <para>
    /// Each skill is built with <see cref="McpServerSkill.CreateFromDirectory(string)"/>: files are read once,
    /// digests are computed from the bytes served, and symbolic links are rejected. Only immediate subdirectories
    /// are considered skills; a <c>SKILL.md</c> nested deeper inside a skill is one of that skill's files.
    /// </para>
    /// <para>
    /// A subdirectory without a <c>SKILL.md</c> is not a skill and is ignored, so shared assets or documentation can
    /// live alongside skills. A subdirectory with a <c>SKILL.md</c> is a skill, and a skill that fails validation is
    /// an error rather than a skipped entry, so that a broken skill is noticed at startup instead of being silently
    /// absent from the catalog.
    /// </para>
    /// <para>
    /// Every caller sees every skill. <c>skills/list</c> and <c>skills/get</c> are raw request handlers and do not
    /// pass through the request filters that guard the built-in resource methods, such as the ASP.NET Core
    /// authorization filters. When some callers must not see some skills, implement <see cref="IMcpSkillCatalog"/>
    /// and use <see cref="WithSkills(IMcpServerBuilder, IMcpSkillCatalog, Action{McpSkillsOptions})"/>, and guard
    /// the corresponding file resources separately.
    /// </para>
    /// </remarks>
    public static IMcpServerBuilder WithSkillsFromDirectory(
        this IMcpServerBuilder builder,
        string directoryPath,
        string uriPrefix = "skill://",
        Action<McpSkillsOptions>? configure = null)
    {
#if NET
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(directoryPath);
        ArgumentNullException.ThrowIfNull(uriPrefix);
#else
        if (builder is null) throw new ArgumentNullException(nameof(builder));
        if (directoryPath is null) throw new ArgumentNullException(nameof(directoryPath));
        if (uriPrefix is null) throw new ArgumentNullException(nameof(uriPrefix));
#endif

        if (uriPrefix.Length == 0)
        {
            throw new ArgumentException("The URI prefix must not be empty.", nameof(uriPrefix));
        }

        if (!uriPrefix.EndsWith("/", StringComparison.Ordinal))
        {
            uriPrefix += "/";
        }

        string fullDirectory = Path.GetFullPath(directoryPath);
        if (!Directory.Exists(fullDirectory))
        {
            throw new DirectoryNotFoundException($"The skills directory '{fullDirectory}' does not exist.");
        }

        var skillDirectories = new List<string>(Directory.EnumerateDirectories(fullDirectory));
        skillDirectories.Sort(StringComparer.Ordinal);

        var skills = new List<McpServerSkill>();
        foreach (string skillDirectory in skillDirectories)
        {
            // The per-skill loader rejects links inside a skill; the same rule applies to the skill directory
            // itself, which could otherwise be a link to a directory outside the skills root.
            if ((File.GetAttributes(skillDirectory) & FileAttributes.ReparsePoint) != 0)
            {
                throw new ArgumentException(
                    $"'{skillDirectory}' is a symbolic link or other reparse point. Links are not followed when loading skills, " +
                    "because a link can point outside the skills directory. Replace it with a regular directory.",
                    nameof(directoryPath));
            }

            if (!File.Exists(Path.Combine(skillDirectory, SkillsProtocol.SkillFileName)))
            {
                continue;
            }

            skills.Add(McpServerSkill.CreateFromDirectory(skillDirectory, uriPrefix, nameof(directoryPath)));
        }

        if (skills.Count == 0)
        {
            throw new ArgumentException(
                $"No skill directories were found under '{fullDirectory}'. Each skill must be an immediate subdirectory containing a {SkillsProtocol.SkillFileName}.",
                nameof(directoryPath));
        }

        return WithSkills(builder, skills, configure);
    }

    /// <summary>
    /// Enables MCP Skills support backed by the specified catalog.
    /// </summary>
    /// <param name="builder">The server builder.</param>
    /// <param name="catalog">The catalog supplying the skills this server serves.</param>
    /// <param name="configure">An optional callback that configures the extension's behavior.</param>
    /// <returns>The builder provided in <paramref name="builder"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> or <paramref name="catalog"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// This registers <c>skills/list</c> and <c>skills/get</c> and declares the extension in the server's
    /// capabilities. Declaring the extension commits the server to both methods.
    /// </para>
    /// <para>
    /// A catalog supplies entries only. The skills' files must be served as ordinary resources through
    /// <c>resources/read</c>, so register them as well (for example with <c>WithResources</c>). To have this done
    /// automatically, build the skills with <see cref="McpServerSkill"/> and use the
    /// <see cref="WithSkills(IMcpServerBuilder, IEnumerable{McpServerSkill}, Action{McpSkillsOptions})"/> overload.
    /// </para>
    /// <para>
    /// <c>skills/list</c> and <c>skills/get</c> are registered as raw request handlers and do not pass through the
    /// request filters that guard the built-in resource methods, such as the ASP.NET Core authorization filters.
    /// When some callers must not see some skills, the catalog decides from the <see cref="McpSkillRequestContext"/>
    /// it receives, and the corresponding file resources must be guarded separately.
    /// </para>
    /// </remarks>
    public static IMcpServerBuilder WithSkills(
        this IMcpServerBuilder builder,
        IMcpSkillCatalog catalog,
        Action<McpSkillsOptions>? configure = null)
    {
#if NET
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(catalog);
#else
        if (builder is null) throw new ArgumentNullException(nameof(builder));
        if (catalog is null) throw new ArgumentNullException(nameof(catalog));
#endif

        var options = new McpSkillsOptions();
        configure?.Invoke(options);

        builder.Services.AddSingleton<IConfigureOptions<McpServerOptions>>(
            _ => new McpSkillsConfigureOptions(catalog, options));

        return builder;
    }

    private sealed class McpSkillsConfigureOptions(IMcpSkillCatalog catalog, McpSkillsOptions skillsOptions)
        : IConfigureOptions<McpServerOptions>
    {
        public void Configure(McpServerOptions options)
        {
#if NET
            ArgumentNullException.ThrowIfNull(options);
#else
            if (options is null) throw new ArgumentNullException(nameof(options));
#endif

            options.Capabilities ??= new ServerCapabilities();
            options.Capabilities.Extensions ??= new Dictionary<string, object>();
            if (!options.Capabilities.Extensions.ContainsKey(SkillsProtocol.ExtensionId))
            {
                options.Capabilities.Extensions[SkillsProtocol.ExtensionId] = new JsonObject();
            }

            // A server declaring the skills extension must also declare the resources capability, since skill
            // files are read through resources/read.
            options.Capabilities.Resources ??= new ResourcesCapability();

            options.RequestHandlers ??= new List<McpServerRequestHandler>();
            options.RequestHandlers.Add(new McpServerRequestHandler
            {
                Method = SkillsProtocol.MethodSkillsList,
                Handler = HandleListSkillsAsync,
            });
            options.RequestHandlers.Add(new McpServerRequestHandler
            {
                // RoutingNameParameter is deliberately left unset. The specification defines no Mcp-Name header
                // mapping for skills/get, so requiring one on Streamable HTTP would reject conforming clients.
                Method = SkillsProtocol.MethodSkillsGet,
                Handler = HandleGetSkillAsync,
            });
        }

        private async ValueTask<JsonNode?> HandleListSkillsAsync(JsonRpcRequest request, CancellationToken cancellationToken)
        {
            var requestParams = DeserializeParams(request, McpSkillsJsonContext.Default.ListSkillsRequestParams);
            var page = await catalog.ListAsync(requestParams?.Cursor, new McpSkillRequestContext(request), cancellationToken).ConfigureAwait(false);
            foreach (var entry in page.Skills)
            {
                ValidateCatalogEntry(entry);
            }

            var result = new ListSkillsResult
            {
                Skills = [.. page.Skills],
                NextCursor = page.NextCursor,
            };

            // resultType, ttlMs, and cacheScope are 2026-07-28 result fields. Earlier revisions reject them as
            // unrecognized keys (issue #1721), so they are only emitted when the request was negotiated under
            // 2026-07-28 or later, where ttlMs and cacheScope are required and default to the conservative
            // "immediately stale, not shareable" values the SDK uses for the built-in list methods.
            if (IsJuly2026OrLaterProtocolRequest(request))
            {
                result.ResultType = "complete";
                result.TimeToLive = skillsOptions.TimeToLive ?? TimeSpan.Zero;
                result.CacheScope = skillsOptions.CacheScope ?? CacheScope.Private;
            }

            return JsonSerializer.SerializeToNode(result, McpSkillsJsonContext.Default.ListSkillsResult);
        }

        private async ValueTask<JsonNode?> HandleGetSkillAsync(JsonRpcRequest request, CancellationToken cancellationToken)
        {
            var requestParams = DeserializeParams(request, McpSkillsJsonContext.Default.GetSkillRequestParams);
            if (string.IsNullOrEmpty(requestParams?.Uri))
            {
                throw new McpProtocolException("The 'uri' parameter is required.", McpErrorCode.InvalidParams);
            }

            var skill = await catalog.GetAsync(requestParams!.Uri, new McpSkillRequestContext(request), cancellationToken).ConfigureAwait(false) ??
                throw new McpProtocolException($"No skill is served at '{requestParams.Uri}'.", McpErrorCode.InvalidParams);
            ValidateCatalogEntry(skill);
            if (!string.Equals(skill.Uri, requestParams.Uri, StringComparison.Ordinal))
            {
                throw new McpProtocolException(
                    $"The skill catalog answered a request for '{requestParams.Uri}' with the entry for '{skill.Uri}'.",
                    McpErrorCode.InternalError);
            }

            var result = new GetSkillResult { Skill = skill };
            if (IsJuly2026OrLaterProtocolRequest(request))
            {
                result.ResultType = "complete";
            }

            return JsonSerializer.SerializeToNode(result, McpSkillsJsonContext.Default.GetSkillResult);
        }

        /// <summary>
        /// A custom catalog is trusted to answer, but not to be correct: an invalid entry is a server bug, reported
        /// as an internal error rather than published to hosts that would have to reject it.
        /// </summary>
        private static void ValidateCatalogEntry(Skill? entry)
        {
            try
            {
                SkillValidation.Validate(entry!, "entry");
            }
            catch (ArgumentException e)
            {
                throw new McpProtocolException($"The skill catalog returned an invalid entry: {e.Message}", McpErrorCode.InternalError);
            }
        }

        private static T? DeserializeParams<T>(JsonRpcRequest request, JsonTypeInfo<T> typeInfo) where T : class
        {
            if (request.Params is null)
            {
                return null;
            }

            try
            {
                return JsonSerializer.Deserialize(request.Params, typeInfo);
            }
            catch (JsonException e)
            {
                throw new McpProtocolException($"Invalid parameters for '{request.Method}': {e.Message}", McpErrorCode.InvalidParams);
            }
        }

        /// <summary>
        /// Returns whether the request was negotiated under the 2026-07-28 protocol revision or later. Under that
        /// revision every request carries its protocol version; requests from an <c>initialize</c>-handshake
        /// session (2025-11-25 and earlier) carry none, so a missing version means an earlier revision.
        /// </summary>
        private static bool IsJuly2026OrLaterProtocolRequest(JsonRpcRequest request) =>
            McpProtocolVersions.IsJuly2026OrLaterProtocolVersion(request.Context?.ProtocolVersion);
    }
}
