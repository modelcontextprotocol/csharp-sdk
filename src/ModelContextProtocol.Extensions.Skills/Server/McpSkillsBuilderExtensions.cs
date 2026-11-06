using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;

namespace ModelContextProtocol.Extensions.Skills;

/// <summary>Extension methods that enable MCP Skills (SEP-2640) support.</summary>
[Experimental(Experimentals.Skills_DiagnosticId, UrlFormat = Experimentals.Skills_Url)]
public static class McpSkillsBuilderExtensions
{
    /// <summary>Enables Skills with registered primitives and optional custom request handlers.</summary>
    /// <param name="builder">The server builder.</param>
    /// <param name="configure">An optional callback configuring the collection and handlers.</param>
    /// <returns>The supplied builder.</returns>
    /// <remarks>
    /// <para>
    /// Calls contribute to the same <see cref="McpSkillsOptions"/>, configured in registration order. Each wire
    /// handler is installed once. A later callback can replace or clear an earlier handler; independent cursors
    /// are never composed automatically. The default handlers list and retrieve <see cref="McpSkillsOptions.SkillCollection"/>.
    /// Registered entries augment the initial custom list page. Registered get requests are answered directly;
    /// the custom get handler is a fallback. Typed Skills filters wrap both composed handlers in registration
    /// order, with get matching performed before filters run. The SDK validates and serializes their results.
    /// </para>
    /// <para>
    /// This method registers Skills only. File resources are registered independently, directly or through
    /// <see cref="WithSkillPackages"/>. Removing or replacing a skill does not change resource availability.
    /// Applications own the agreement between entries and any independently registered or overridden resources.
    /// </para>
    /// <para>
    /// Handlers and filters receive the existing request's services and principal. No provider or additional scope is created
    /// by Skills. Options-only <see cref="McpServer.Create"/> works without services when handlers need none.
    /// Skills requests do not pass through resource request filters; protect metadata and files with the same
    /// application policy. Server-local primitive metadata is not interpreted as authorization.
    /// </para>
    /// </remarks>
    [Experimental(Experimentals.Skills_DiagnosticId, UrlFormat = Experimentals.Skills_Url)]
    public static IMcpServerBuilder WithSkills(this IMcpServerBuilder builder, Action<McpSkillsOptions>? configure = null)
    {
#if NET
        ArgumentNullException.ThrowIfNull(builder);
#else
        if (builder is null) throw new ArgumentNullException(nameof(builder));
#endif

        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IConfigureOptions<McpSkillsOptions>, SkillsOptionsSetup>());
        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IConfigureOptions<McpServerOptions>, McpSkillsConfigureOptions>());
        if (configure is not null)
        {
            builder.Services.Configure(configure);
        }

        return builder;
    }

    /// <summary>Enables Skills with the supplied server-side primitives.</summary>
    /// <param name="builder">The server builder.</param>
    /// <param name="skills">The primitives to add to the default collection.</param>
    /// <returns>The supplied builder.</returns>
    /// <remarks>
    /// Repeated calls contribute primitives with <c>TryAdd</c>, as resources do. The first accepted identity wins,
    /// without replacement. No file resources are registered. Configure runtime behavior separately through
    /// <see cref="WithSkills(IMcpServerBuilder, Action{McpSkillsOptions})"/>.
    /// </remarks>
    [Experimental(Experimentals.Skills_DiagnosticId, UrlFormat = Experimentals.Skills_Url)]
    public static IMcpServerBuilder WithSkills(
        this IMcpServerBuilder builder,
        IEnumerable<McpServerSkill> skills)
    {
#if NET
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(skills);
#else
        if (builder is null) throw new ArgumentNullException(nameof(builder));
        if (skills is null) throw new ArgumentNullException(nameof(skills));
#endif

        foreach (var skill in skills)
        {
            if (skill is not null)
            {
                builder.Services.AddSingleton(skill);
            }
        }

        return WithSkills(builder);
    }

    /// <summary>Enables Skills and independently registers the resources of a fully validated package batch.</summary>
    /// <param name="builder">The server builder.</param>
    /// <param name="packages">The authored packages to register.</param>
    /// <returns>The supplied builder.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> or <paramref name="packages"/> is null.</exception>
    /// <exception cref="ArgumentException">A package is null or the batch contains conflicting file manifests.</exception>
    /// <remarks>
    /// The input is fully materialized and its manifests validated before services are added. An empty batch
    /// enables Skills. Components use ordinary first-wins primitive/resource registration, independently of
    /// final collection membership. Separate calls and application resources have no package-provenance guard.
    /// Applications own manifest/content agreement after overrides. Configure runtime behavior with
    /// <see cref="WithSkills(IMcpServerBuilder, Action{McpSkillsOptions})"/>.
    /// </remarks>
    [Experimental(Experimentals.Skills_DiagnosticId, UrlFormat = Experimentals.Skills_Url)]
    public static IMcpServerBuilder WithSkillPackages(this IMcpServerBuilder builder, IEnumerable<McpServerSkillPackage> packages)
    {
#if NET
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(packages);
#else
        if (builder is null) throw new ArgumentNullException(nameof(builder));
        if (packages is null) throw new ArgumentNullException(nameof(packages));
#endif

        var materialized = packages.ToArray();
        McpServerSkillPackage.ValidatePackages(materialized, nameof(packages));
        builder.WithSkills(materialized.Select(package => package.Skill));
        builder.WithResources(materialized.SelectMany(package => package.Resources));
        return builder;
    }

    /// <summary>Authors and registers one skill directory and its resources.</summary>
    /// <param name="builder">The server builder.</param>
    /// <param name="directoryPath">A skill directory containing <c>SKILL.md</c>.</param>
    /// <param name="options">Optional package-authoring settings.</param>
    /// <returns>The supplied builder.</returns>
    /// <remarks>Authoring is synchronous and eager. See <see cref="McpServerSkillPackage.CreateFromDirectory"/>.</remarks>
    [Experimental(Experimentals.Skills_DiagnosticId, UrlFormat = Experimentals.Skills_Url)]
    public static IMcpServerBuilder WithSkillFromDirectory(
        this IMcpServerBuilder builder, string directoryPath, McpServerSkillPackageCreateOptions? options = null)
    {
        if (builder is null) throw new ArgumentNullException(nameof(builder));
        return builder.WithSkillPackages([McpServerSkillPackage.CreateFromDirectory(directoryPath, options)]);
    }

    /// <summary>Authors and registers packages from immediate skill subdirectories.</summary>
    /// <param name="builder">The server builder.</param>
    /// <param name="directoryPath">The parent directory whose children contain skills.</param>
    /// <param name="options">Optional common package-discovery settings.</param>
    /// <returns>The supplied builder.</returns>
    /// <remarks>Authoring and batch validation finish before registration. See <see cref="McpServerSkillPackage.CreateFromSubdirectories"/>.</remarks>
    [Experimental(Experimentals.Skills_DiagnosticId, UrlFormat = Experimentals.Skills_Url)]
    public static IMcpServerBuilder WithSkillsFromSubdirectories(
        this IMcpServerBuilder builder, string directoryPath, McpServerSkillPackageDiscoveryOptions? options = null)
    {
        if (builder is null) throw new ArgumentNullException(nameof(builder));
        return builder.WithSkillPackages(McpServerSkillPackage.CreateFromSubdirectories(directoryPath, options));
    }

    private sealed class SkillsOptionsSetup(IEnumerable<McpServerSkill> skills) : IConfigureOptions<McpSkillsOptions>
    {
        public void Configure(McpSkillsOptions options)
        {
            if (options.SkillCollection is null)
            {
                throw new InvalidOperationException("McpSkillsOptions.SkillCollection must not be null.");
            }

            foreach (var skill in skills)
            {
                options.SkillCollection.TryAdd(skill);
            }
        }
    }

    private sealed class McpSkillsConfigureOptions : IConfigureOptions<McpServerOptions>
    {
        private readonly McpSkillsOptions _skillsOptions;
        private readonly McpRequestHandler<ListSkillsRequestParams, ListSkillsResult> _listSkillsHandler;
        private readonly McpRequestHandler<GetSkillRequestParams, GetSkillResult> _getSkillHandler;

        public McpSkillsConfigureOptions(IOptions<McpSkillsOptions> configuredOptions)
        {
            _skillsOptions = configuredOptions.Value;
            _listSkillsHandler = BuildFilterPipeline(ListSkillsAsync, _skillsOptions.ListSkillsFilters);
            _getSkillHandler = BuildFilterPipeline(GetSkillAsync, _skillsOptions.GetSkillFilters);

            var options = _skillsOptions;
            if (options.SkillCollection is null)
            {
                throw new InvalidOperationException("McpSkillsOptions.SkillCollection must not be null.");
            }

            foreach (var skill in options.SkillCollection)
            {
                if (skill.ProtocolSkill is { } entry)
                {
                    SkillValidation.Validate(entry, nameof(McpSkillsOptions.SkillCollection));
                    if (!SkillUriComparer.Instance.Equals(entry.Uri, skill.Id))
                    {
                        throw new ArgumentException(
                            $"Skill '{skill.Id}' contributes a listing entry for '{entry.Uri}'.", nameof(McpSkillsOptions.SkillCollection));
                    }
                }
            }
        }

        public void Configure(McpServerOptions options)
        {
            options.Capabilities ??= new ServerCapabilities();
            options.Capabilities.Extensions ??= new Dictionary<string, object>();
            if (!options.Capabilities.Extensions.ContainsKey(SkillsProtocol.ExtensionId))
            {
                options.Capabilities.Extensions[SkillsProtocol.ExtensionId] = new JsonObject();
            }

            options.Capabilities.Resources ??= new ResourcesCapability();

            options.RequestHandlers ??= new List<McpServerRequestHandler>();
            options.RequestHandlers.Add(new()
            {
                Method = SkillsProtocol.MethodSkillsList,
                Handler = HandleListSkillsAsync,
            });

            options.RequestHandlers.Add(new()
            {
                // SEP-2640 defines no Mcp-Name mapping for skills/get.
                Method = SkillsProtocol.MethodSkillsGet,
                Handler = HandleGetSkillAsync,
            });

        }

        private async ValueTask<JsonNode?> HandleListSkillsAsync(RequestContext<JsonNode?> context, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var parameters = DeserializeParams(context.Params, McpSkillsJsonContext.Default.ListSkillsRequestParams) ?? new();
            var typedContext = new RequestContext<ListSkillsRequestParams>(context.Server, context.JsonRpcRequest, parameters)
            {
                Services = context.Services,
            };

            var result = ValidateListResult(
                await _listSkillsHandler(typedContext, cancellationToken).ConfigureAwait(false));
            return SerializeResult(result, McpSkillsJsonContext.Default.ListSkillsResult,
                context.JsonRpcRequest.Context?.ProtocolVersion);
        }

        private async ValueTask<ListSkillsResult> ListSkillsAsync(
            RequestContext<ListSkillsRequestParams> context, CancellationToken cancellationToken)
        {
            var handler = _skillsOptions.ListSkillsHandler;
            var page = handler is not null ?
                await handler(context, cancellationToken).ConfigureAwait(false) :
                new ListSkillsResult();
            if (page?.Skills is null)
            {
                throw new InvalidOperationException("The Skills list handler returned no page or skills collection.");
            }

            if (context.Params?.Cursor is null)
            {
                foreach (var skill in _skillsOptions.SkillCollection)
                {
                    if (skill.ProtocolSkill is { } entry)
                    {
                        if (!SkillUriComparer.Instance.Equals(entry.Uri, skill.Id))
                        {
                            throw new InvalidOperationException(
                                $"Skill '{skill.Id}' contributes a listing entry for '{entry.Uri}'.");
                        }

                        page.Skills.Add(entry);
                    }
                }
            }

            return page;
        }

        private async ValueTask<JsonNode?> HandleGetSkillAsync(RequestContext<JsonNode?> context, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var parameters = DeserializeParams(context.Params, McpSkillsJsonContext.Default.GetSkillRequestParams);
            if (parameters is null || string.IsNullOrEmpty(parameters.Uri))
            {
                throw new McpProtocolException("The 'uri' parameter is required.", McpErrorCode.InvalidParams);
            }

            string uri = parameters.Uri;
            _skillsOptions.SkillCollection.TryGetPrimitive(uri, out var primitive);
            var typedContext = new RequestContext<GetSkillRequestParams>(context.Server, context.JsonRpcRequest, parameters)
            {
                Services = context.Services,
                MatchedPrimitive = primitive,
            };

            var result = await _getSkillHandler(typedContext, cancellationToken).ConfigureAwait(false);
            if (result?.Skill is null)
            {
                throw new InvalidOperationException("The Skills pipeline returned no skill entry.");
            }

            var entry = result.Skill;
            SkillValidation.Validate(entry, "entry");
            if (!SkillUriComparer.Instance.Equals(entry.Uri, uri))
            {
                throw new InvalidOperationException(
                    $"The Skills get handler answered a request for '{uri}' with the entry for '{entry.Uri}'.");
            }

            return SerializeResult(result, McpSkillsJsonContext.Default.GetSkillResult,
                context.JsonRpcRequest.Context?.ProtocolVersion);
        }

        private ValueTask<GetSkillResult> GetSkillAsync(
            RequestContext<GetSkillRequestParams> context, CancellationToken cancellationToken)
        {
            if (context.MatchedPrimitive is McpServerSkill primitive)
            {
                return primitive.GetAsync(context, cancellationToken);
            }

            return _skillsOptions.GetSkillHandler is { } handler ? handler(context, cancellationToken) :
                throw new McpProtocolException($"No skill is served at '{context.Params.Uri}'.", McpErrorCode.InvalidParams);
        }

        private static ListSkillsResult ValidateListResult(ListSkillsResult? page)
        {
            if (page?.Skills is null)
            {
                throw new InvalidOperationException("The Skills pipeline returned no page or skills collection.");
            }

            foreach (var entry in page.Skills)
            {
                if (entry is null)
                {
                    throw new InvalidOperationException("The Skills pipeline returned a null list entry.");
                }

                SkillValidation.Validate(entry, "entry");
            }

            return page;
        }

        private static McpRequestHandler<TParams, TResult> BuildFilterPipeline<TParams, TResult>(
            McpRequestHandler<TParams, TResult> handler, IList<McpRequestFilter<TParams, TResult>> filters)
        {
            for (int i = filters.Count - 1; i >= 0; i--)
            {
                handler = filters[i](handler);
            }

            return handler;
        }

        private static T? DeserializeParams<T>(JsonNode? parameters, JsonTypeInfo<T> typeInfo)
            where T : class
        {
            try
            {
                return parameters?.Deserialize(typeInfo);
            }
            catch (JsonException error)
            {
                throw new McpProtocolException("Invalid Skills parameters.", error, McpErrorCode.InvalidParams);
            }
        }

        private static JsonObject SerializeResult<T>(T result, JsonTypeInfo<T> typeInfo, string? protocolVersion)
            where T : Result, ICacheableResult
        {
            var serialized = JsonSerializer.SerializeToNode(result, typeInfo)!.AsObject();
            if (McpProtocolVersions.IsJuly2026OrLaterProtocolVersion(protocolVersion))
            {
                serialized["resultType"] = "complete";
                serialized["ttlMs"] ??= 0L;
                serialized["cacheScope"] ??= "private";
            }
            else
            {
                serialized.Remove("resultType");
                serialized.Remove("ttlMs");
                serialized.Remove("cacheScope");
            }

            return serialized;
        }
    }
}
