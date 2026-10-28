using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.Diagnostics.CodeAnalysis;
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
    /// <exception cref="InvalidOperationException">Skills are already registered for this server.</exception>
    /// <remarks>
    /// <para>
    /// This registers <c>skills/list</c> and <c>skills/get</c> backed by an <see cref="InMemoryMcpSkillCatalog"/>
    /// over the skills' entries, and registers each skill's <see cref="McpServerSkill.Resources"/> so the files
    /// are served through <c>resources/list</c> and <c>resources/read</c>.
    /// </para>
    /// <para>
    /// A server has one Skills registration. Pass every skill to a single call; a second call to this method,
    /// <see cref="WithSkillsFromDirectory(IMcpServerBuilder, string, string, Action{McpSkillsOptions})"/>, or
    /// <see cref="WithSkills(IMcpServerBuilder, IMcpSkillCatalog, Action{McpSkillsOptions})"/> for the same service
    /// collection throws before it registers anything. A call that throws for any other reason registers nothing
    /// either, so a corrected call can follow it.
    /// </para>
    /// <para>
    /// Nested skills may legitimately list the same file. A file URI shared by several skills is registered once,
    /// provided every skill lists it with the same digest and the identical URI text. URIs that differ only in the
    /// case of their scheme or authority are rejected, because the server's resource collection treats them as one.
    /// </para>
    /// <para>
    /// Each file must be the only resource at its URI. When another resource is registered at a URI the server's
    /// resource collection treats as the same, whether before or after this call, building the server's options
    /// throws an <see cref="InvalidOperationException"/> that names the URI, instead of serving one resource in
    /// place of the other.
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

        // Before registering any file resources, so a second registration leaves the service collection unchanged.
        ThrowIfSkillsRegistered(builder);

        var entries = new List<Skill>();

        // The server's resource collection keys concrete resources by System.Uri equality, under which scheme and
        // authority are case-insensitive, and it registers silently. Detect shared files under those same
        // semantics, so two URIs the collection would merge are caught here rather than served as one another.
        var registeredFiles = new Dictionary<Uri, (string Uri, string Digest)>();

        // The registered file resources, keyed exactly as the server's resource collection keys them.
        var skillFiles = new McpServerResourceCollection();

        // Added to the services only after every skill, the catalog, and the configuration are accepted, so a call
        // that throws leaves no resources behind for a corrected retry to conflict with.
        var stagedFiles = new List<McpServerResource>();
        foreach (var skill in skills)
        {
            if (skill is null)
            {
                throw new ArgumentException("The skills must not contain null entries.", nameof(skills));
            }

            var protocolSkill = skill.ProtocolSkill;
            entries.Add(protocolSkill);

            var manifest = protocolSkill.Resources.Resources!;
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
                stagedFiles.Add(skill.Resources[i]);
                skillFiles.TryAdd(skill.Resources[i]);
            }
        }

        WithSkills(builder, new InMemoryMcpSkillCatalog(entries), configure);
        foreach (var file in stagedFiles)
        {
            builder.Services.AddSingleton(file);
        }

        // Other resources are known only once the server's options are built, so the check runs then.
        builder.Services.AddSingleton<IPostConfigureOptions<McpServerOptions>>(
            services => new SkillFileConflictCheck(skillFiles, services.GetServices<McpServerResource>()));

        return builder;
    }

    /// <summary>
    /// Enables MCP Skills support for every skill directory found directly under <paramref name="directoryPath"/>,
    /// serving both their entries and their files, excluding dot-prefixed, hidden, and system entries.
    /// </summary>
    /// <param name="builder">The server builder.</param>
    /// <param name="directoryPath">
    /// A directory whose immediate subdirectories are skills. Each included subdirectory containing a <c>SKILL.md</c> becomes
    /// one skill; subdirectories without one are ignored.
    /// </param>
    /// <param name="uriPrefix">
    /// The prefix of each skill's URI. A skill's <c>SKILL.md</c> is published at <c>{uriPrefix}{name}/SKILL.md</c>,
    /// where <c>name</c> is the skill's frontmatter name. Defaults to <c>skill://</c>; use a longer prefix such as
    /// <c>skill://acme/billing/</c> to place the skills under an organizational path. Names used as path segments
    /// are percent-encoded.
    /// </param>
    /// <param name="configure">An optional callback that configures the extension's behavior.</param>
    /// <returns>The builder provided in <paramref name="builder"/>.</returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    /// <exception cref="DirectoryNotFoundException"><paramref name="directoryPath"/> does not exist.</exception>
    /// <exception cref="ArgumentException">
    /// No subdirectory contains a <c>SKILL.md</c>, a skill directory is invalid (see
    /// <see cref="McpServerSkill.CreateFromDirectory(string)"/>), or two skills declare the same name.
    /// </exception>
    /// <exception cref="InvalidOperationException">Skills are already registered for this server.</exception>
    /// <remarks>
    /// <para>
    /// Each skill is built with <see cref="McpServerSkill.CreateFromDirectory(string)"/>: files are read once,
    /// digests are computed from the bytes served, symbolic links are rejected, and frontmatter the SDK's reader
    /// cannot read is an error. Only immediate subdirectories are considered skills; a <c>SKILL.md</c> nested deeper inside a skill is one
    /// of that skill's files.
    /// </para>
    /// <para>
    /// A subdirectory without a <c>SKILL.md</c> is not a skill and is ignored, so shared assets or documentation can
    /// live alongside skills. A subdirectory with a <c>SKILL.md</c> is a skill, and a skill that fails validation is
    /// an error rather than a skipped entry, so that a broken skill is noticed at startup instead of being silently
    /// absent from the catalog.
    /// </para>
    /// <para>
    /// A server has one Skills registration, so this method can be called once. To serve skills from several
    /// directories, build them with <see cref="McpServerSkill.CreateFromDirectory(string)"/> and pass all of them to
    /// one <see cref="WithSkills(IMcpServerBuilder, IEnumerable{McpServerSkill}, Action{McpSkillsOptions})"/> call.
    /// The skills' files are registered as described there, including the check that each file is the only
    /// resource at its URI.
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
        Action<McpSkillsOptions>? configure = null) =>
        WithSkillsFromDirectory(builder, directoryPath, includeHiddenAndSystemFiles: false, uriPrefix, configure);

    /// <summary>
    /// Enables skills from immediate subdirectories with an explicit hidden/system-file policy.
    /// </summary>
    /// <param name="builder">The server builder.</param>
    /// <param name="directoryPath">The directory containing skill directories.</param>
    /// <param name="includeHiddenAndSystemFiles">Whether to include dot-prefixed, hidden, and system entries.</param>
    /// <param name="uriPrefix">The prefix used to derive skill URIs.</param>
    /// <param name="configure">An optional callback configuring the extension.</param>
    /// <returns>The supplied builder.</returns>
    /// <exception cref="InvalidOperationException">Skills are already registered for this server.</exception>
    /// <remarks>
    /// The same selection applies to skill directories and their contents. Included symbolic links remain errors.
    /// See <see cref="WithSkillsFromDirectory(IMcpServerBuilder, string, string, Action{McpSkillsOptions})"/>.
    /// </remarks>
    public static IMcpServerBuilder WithSkillsFromDirectory(
        this IMcpServerBuilder builder,
        string directoryPath,
        bool includeHiddenAndSystemFiles,
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

        // Before reading the directory, so a second registration reports this rather than an error about the directory.
        ThrowIfSkillsRegistered(builder);

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
            if (!includeHiddenAndSystemFiles &&
                Path.GetFileName(skillDirectory).StartsWith(".", StringComparison.Ordinal))
            {
                continue;
            }
            var attributes = File.GetAttributes(skillDirectory);
            if (!includeHiddenAndSystemFiles &&
                (attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0)
            {
                continue;
            }
            // The per-skill loader rejects links inside a skill; the same rule applies to the skill directory
            // itself, which could otherwise be a link to a directory outside the skills root.
            if ((attributes & FileAttributes.ReparsePoint) != 0)
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

            skills.Add(McpServerSkill.CreateFromDirectory(
                skillDirectory, uriPrefix, nameof(directoryPath), includeHiddenAndSystemFiles));
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
    /// <exception cref="InvalidOperationException">Skills are already registered for this server.</exception>
    /// <remarks>
    /// <para>
    /// This registers <c>skills/list</c> and <c>skills/get</c> and declares the extension in the server's
    /// capabilities. Declaring the extension commits the server to both methods.
    /// </para>
    /// <para>
    /// A server has one Skills registration, and so one catalog. A second call to this method or to the other
    /// <c>WithSkills</c> and <c>WithSkillsFromDirectory</c> overloads for the same service collection throws. To serve
    /// skills from several sources, implement one <see cref="IMcpSkillCatalog"/> that combines them.
    /// </para>
    /// <para>
    /// A catalog supplies entries only. The skills' files must be served as ordinary resources through
    /// <c>resources/read</c>, so register them as well (for example with <c>WithResources</c>). To have this done
    /// automatically, build the skills with <see cref="McpServerSkill"/> and use the
    /// <see cref="WithSkills(IMcpServerBuilder, IEnumerable{McpServerSkill}, Action{McpSkillsOptions})"/> overload.
    /// </para>
    /// <para>
    /// The supplied instance is shared across requests and remains owned by the caller; the server does not
    /// dispose it. It must not capture scoped services. Resolve them from
    /// <see cref="MessageContext.Services"/> on the supplied <see cref="RequestContext{TParams}"/>, or use a
    /// dependency-injection registration overload.
    /// </para>
    /// <para>
    /// The configured handlers retain the supplied instance, which is also registered as <see cref="IMcpSkillCatalog"/>.
    /// A server created with <see cref="McpServer.Create"/> can use these options without a service provider
    /// when the catalog and its resource handlers do not require services. In that case,
    /// <see cref="MessageContext.Services"/> on the request context is <see langword="null"/>.
    /// </para>
    /// <para>
    /// <c>skills/list</c> and <c>skills/get</c> are registered as raw request handlers and do not pass through the
    /// request filters that guard the built-in resource methods, such as the ASP.NET Core authorization filters.
    /// When some callers must not see some skills, the catalog decides from the <see cref="RequestContext{TParams}"/>
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

        return WithSkillsCore(builder, new ServiceDescriptor(typeof(IMcpSkillCatalog), catalog), configure);
    }

    /// <summary>
    /// Enables MCP Skills support with a catalog created by dependency injection.
    /// </summary>
    /// <typeparam name="TCatalog">The catalog implementation type.</typeparam>
    /// <param name="builder">The server builder.</param>
    /// <param name="lifetime">The catalog's dependency injection lifetime. Defaults to transient.</param>
    /// <param name="configure">An optional callback that configures the extension's behavior.</param>
    /// <returns>The builder provided in <paramref name="builder"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">Skills are already registered for this server.</exception>
    /// <remarks>
    /// <para>
    /// The catalog is registered as <see cref="IMcpSkillCatalog"/> and resolved from the request's services when
    /// a Skills method is invoked, not when the server's options are configured. Scoped and transient catalogs
    /// can capture scoped dependencies. Singleton catalogs must not capture them; they can instead resolve
    /// them from <see cref="MessageContext.Services"/> on the supplied <see cref="RequestContext{TParams}"/>.
    /// When creating a server with <see cref="McpServer.Create"/> directly, supply the corresponding service provider.
    /// </para>
    /// <para>
    /// The request uses the same service scope lifecycle as built-in MCP handlers. Stateless HTTP uses the
    /// existing HTTP request scope. Session-based transports create an MCP request scope when
    /// <see cref="McpServerOptions.ScopeRequests"/> is enabled. If it is disabled, the caller must supply a
    /// suitably scoped provider. The dependency injection container owns the catalog and its dependencies;
    /// any scope created by the MCP server is disposed asynchronously after the handler completes.
    /// </para>
    /// <para>
    /// A catalog supplies entries only; register the corresponding file resources separately.
    /// Skills methods do not pass through the typed request filters, so the catalog must enforce any
    /// per-caller visibility policy, and the resources must be guarded separately.
    /// </para>
    /// </remarks>
    public static IMcpServerBuilder WithSkills<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TCatalog>(
        this IMcpServerBuilder builder,
        ServiceLifetime lifetime = ServiceLifetime.Transient,
        Action<McpSkillsOptions>? configure = null)
        where TCatalog : class, IMcpSkillCatalog
    {
#if NET
        ArgumentNullException.ThrowIfNull(builder);
#else
        if (builder is null) throw new ArgumentNullException(nameof(builder));
#endif

        return WithSkillsCore(builder, new ServiceDescriptor(typeof(IMcpSkillCatalog), typeof(TCatalog), lifetime), configure);
    }

    /// <summary>
    /// Enables MCP Skills support with a catalog created by a dependency injection factory.
    /// </summary>
    /// <param name="builder">The server builder.</param>
    /// <param name="catalogFactory">The factory, invoked with the provider selected by the catalog's service lifetime.</param>
    /// <param name="lifetime">The catalog's dependency injection lifetime. Defaults to transient.</param>
    /// <param name="configure">An optional callback that configures the extension's behavior.</param>
    /// <returns>The builder provided in <paramref name="builder"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> or <paramref name="catalogFactory"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">Skills are already registered for this server.</exception>
    /// <remarks>
    /// The registration, request scope, ownership, and authorization behavior is the same as
    /// <see cref="WithSkills{TCatalog}(IMcpServerBuilder, ServiceLifetime, Action{McpSkillsOptions})"/>.
    /// A singleton factory receives the root provider and must not resolve scoped services from it.
    /// </remarks>
    public static IMcpServerBuilder WithSkills(
        this IMcpServerBuilder builder,
        Func<IServiceProvider, IMcpSkillCatalog> catalogFactory,
        ServiceLifetime lifetime = ServiceLifetime.Transient,
        Action<McpSkillsOptions>? configure = null)
    {
#if NET
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(catalogFactory);
#else
        if (builder is null) throw new ArgumentNullException(nameof(builder));
        if (catalogFactory is null) throw new ArgumentNullException(nameof(catalogFactory));
#endif

        return WithSkillsCore(builder, new ServiceDescriptor(typeof(IMcpSkillCatalog), catalogFactory, lifetime), configure);
    }

    private static IMcpServerBuilder WithSkillsCore(
        IMcpServerBuilder builder,
        ServiceDescriptor registration,
        Action<McpSkillsOptions>? configure)
    {
        ThrowIfSkillsRegistered(builder);

        var options = new McpSkillsOptions();
        configure?.Invoke(options);

        builder.Services.Add(registration);
        builder.Services.AddSingleton(new SkillsRegistrationMarker());
        builder.Services.AddSingleton<IConfigureOptions<McpServerOptions>>(
            new McpSkillsConfigureOptions(options, registration.ImplementationInstance is IMcpSkillCatalog catalog ? catalog : null));

        return builder;
    }

    /// <summary>
    /// Fails when Skills are already registered for the builder's service collection. A second registration would
    /// install <c>skills/list</c> and <c>skills/get</c> again, which the server rejects only when it is created, with
    /// an error that does not mention Skills.
    /// </summary>
    private static void ThrowIfSkillsRegistered(IMcpServerBuilder builder)
    {
        bool isRegistered = builder.Services.Any(descriptor => descriptor.ServiceType == typeof(SkillsRegistrationMarker));
        if (isRegistered)
        {
            throw new InvalidOperationException(
                "Skills are already registered for this server. Call WithSkills or WithSkillsFromDirectory once: pass every " +
                "McpServerSkill to a single WithSkills call, or implement one IMcpSkillCatalog that combines your sources.");
        }
    }

    /// <summary>Marks a service collection whose server has Skills registered.</summary>
    private sealed class SkillsRegistrationMarker;

    /// <summary>
    /// Fails building the server's options when another resource is registered at the URI of a Skills file. The
    /// server's resource collection keeps one resource per URI, so one of the two would be served in place of the
    /// other, whichever was registered first.
    /// </summary>
    private sealed class SkillFileConflictCheck(McpServerResourceCollection skillFiles, IEnumerable<McpServerResource> registeredResources)
        : IPostConfigureOptions<McpServerOptions>
    {
        public void PostConfigure(string? name, McpServerOptions options)
        {
            // The registered services include a resource the collection dropped because an equivalent URI came first;
            // the collection can also hold resources that were added to it directly.
            var resources = options.ResourceCollection is { } collection ?
                registeredResources.Concat(collection) :
                registeredResources;
            foreach (var resource in resources)
            {
                string uri = resource.ProtocolResourceTemplate.UriTemplate;
                if (skillFiles.TryGetPrimitive(uri, out var file) &&
                    !ReferenceEquals(file, resource))
                {
                    throw new InvalidOperationException(
                        $"The Skills file '{file.ProtocolResourceTemplate.UriTemplate}' conflicts with another resource registered at '{uri}'. " +
                        "The server keeps one resource per URI, so one of them would be served in place of the other. " +
                        "Remove the other resource, or give it a different URI.");
                }
            }
        }
    }

    private sealed class McpSkillsConfigureOptions(McpSkillsOptions skillsOptions, IMcpSkillCatalog? suppliedCatalog)
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

        private async ValueTask<JsonNode?> HandleListSkillsAsync(RequestContext<JsonNode?> context, CancellationToken cancellationToken)
        {
            var request = context.JsonRpcRequest;
            var requestParams = DeserializeParams(context, McpSkillsJsonContext.Default.ListSkillsRequestParams) ?? new();
            var requestCatalog = ResolveCatalog(context);
            var skillContext = new RequestContext<ListSkillsRequestParams>(context.Server, request, requestParams) { Services = context.Services };
            var page = await requestCatalog.ListAsync(skillContext, cancellationToken).ConfigureAwait(false);
            // The sealed built-in catalog validates its owned snapshots at construction.
            if (requestCatalog is not InMemoryMcpSkillCatalog)
            {
                foreach (var entry in page.Skills)
                {
                    ValidateCatalogEntry(entry);
                }
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

        private async ValueTask<JsonNode?> HandleGetSkillAsync(RequestContext<JsonNode?> context, CancellationToken cancellationToken)
        {
            var request = context.JsonRpcRequest;
            var requestParams = DeserializeParams(context, McpSkillsJsonContext.Default.GetSkillRequestParams);
            if (requestParams is null || string.IsNullOrEmpty(requestParams.Uri))
            {
                throw new McpProtocolException("The 'uri' parameter is required.", McpErrorCode.InvalidParams);
            }

            string uri = requestParams.Uri;
            var requestCatalog = ResolveCatalog(context);
            var skillContext = new RequestContext<GetSkillRequestParams>(context.Server, request, requestParams) { Services = context.Services };
            var skill = await requestCatalog.GetAsync(skillContext, cancellationToken).ConfigureAwait(false) ??
                throw new McpProtocolException($"No skill is served at '{uri}'.", McpErrorCode.InvalidParams);
            if (requestCatalog is not InMemoryMcpSkillCatalog)
            {
                ValidateCatalogEntry(skill);
            }
            if (!string.Equals(skill.Uri, uri, StringComparison.Ordinal))
            {
                throw new McpProtocolException(
                    $"The skill catalog answered a request for '{uri}' with the entry for '{skill.Uri}'.",
                    McpErrorCode.InternalError);
            }

            var result = new GetSkillResult { Skill = skill };
            if (IsJuly2026OrLaterProtocolRequest(request))
            {
                result.ResultType = "complete";
                result.TimeToLive = skillsOptions.TimeToLive.GetValueOrDefault();
                result.CacheScope = skillsOptions.CacheScope.GetValueOrDefault(CacheScope.Private);
            }

            return JsonSerializer.SerializeToNode(result, McpSkillsJsonContext.Default.GetSkillResult);
        }

        private IMcpSkillCatalog ResolveCatalog(MessageContext context) =>
            suppliedCatalog ?? (context.Services ??
                throw new InvalidOperationException("Dependency-injected Skills catalogs require a service provider for the request."))
                .GetRequiredService<IMcpSkillCatalog>();

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

        private static T? DeserializeParams<T>(RequestContext<JsonNode?> context, JsonTypeInfo<T> typeInfo) where T : class
        {
            if (context.Params is null)
            {
                return null;
            }

            try
            {
                return JsonSerializer.Deserialize(context.Params, typeInfo);
            }
            catch (JsonException e)
            {
                throw new McpProtocolException($"Invalid parameters for '{context.JsonRpcRequest.Method}': {e.Message}", McpErrorCode.InvalidParams);
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
