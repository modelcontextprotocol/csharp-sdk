using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ModelContextProtocol.Extensions.Skills;

/// <summary>
/// Extension methods for <see cref="IMcpServerBuilder"/> to enable MCP Skills (SEP-2640) support.
/// </summary>
public static class McpSkillsBuilderExtensions
{
    /// <summary>
    /// Enables MCP Skills support backed by the specified catalog.
    /// </summary>
    /// <param name="builder">The server builder.</param>
    /// <param name="catalog">The catalog supplying the skills this server serves.</param>
    /// <returns>The builder provided in <paramref name="builder"/>.</returns>
    public static IMcpServerBuilder WithSkills(this IMcpServerBuilder builder, IMcpSkillCatalog catalog)
        => WithSkills(builder, catalog, static _ => { });

    /// <summary>
    /// Enables MCP Skills support backed by the specified catalog.
    /// </summary>
    /// <param name="builder">The server builder.</param>
    /// <param name="catalog">The catalog supplying the skills this server serves.</param>
    /// <param name="configure">A callback that configures the extension's behavior.</param>
    /// <returns>The builder provided in <paramref name="builder"/>.</returns>
    /// <remarks>
    /// <para>
    /// Declaring the extension commits the server to both <c>skills/list</c> and <c>skills/get</c>. Both are
    /// registered here.
    /// </para>
    /// <para>
    /// This registers the protocol methods only. Serving the skills' file content remains the ordinary
    /// responsibility of the server's resources, so register the skill files as resources as well.
    /// </para>
    /// </remarks>
    public static IMcpServerBuilder WithSkills(
        this IMcpServerBuilder builder,
        IMcpSkillCatalog catalog,
        Action<McpSkillsOptions> configure)
    {
#if NET
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(configure);
#else
        if (builder is null) throw new ArgumentNullException(nameof(builder));
        if (catalog is null) throw new ArgumentNullException(nameof(catalog));
        if (configure is null) throw new ArgumentNullException(nameof(configure));
#endif

        var options = new McpSkillsOptions();
        configure(options);

        builder.Services.AddSingleton<IConfigureOptions<McpServerOptions>>(
            _ => new McpSkillsConfigureOptions(catalog, options));

        return builder;
    }

    private sealed class McpSkillsConfigureOptions(IMcpSkillCatalog catalog, McpSkillsOptions skillsOptions)
        : IConfigureOptions<McpServerOptions>
    {
        public void Configure(McpServerOptions options)
        {
            options.Capabilities ??= new ServerCapabilities();
            options.Capabilities.Extensions ??= new Dictionary<string, object>();
            if (!options.Capabilities.Extensions.ContainsKey(SkillsProtocol.ExtensionId))
            {
                options.Capabilities.Extensions[SkillsProtocol.ExtensionId] = new JsonObject();
            }

            options.RequestHandlers ??= new List<McpServerRequestHandler>();
            options.RequestHandlers.Add(new McpServerRequestHandler
            {
                Method = SkillsProtocol.MethodSkillsList,
                Handler = HandleListSkillsAsync,
            });
            options.RequestHandlers.Add(new McpServerRequestHandler
            {
                Method = SkillsProtocol.MethodSkillsGet,
                // RoutingNameParameter is deliberately left unset. Setting it to "uri" would mirror how
                // Core routes the built-in resources/read, but it also makes the Mcp-Name header mandatory
                // on Streamable HTTP. SEP-2640 defines no such requirement, so a conforming client does not
                // send it and every skills/get request is rejected with 400. Verified against the SEP-2640
                // conformance scenarios.
                Handler = HandleGetSkillAsync,
            });
        }

        private async ValueTask<JsonNode?> HandleListSkillsAsync(
            JsonRpcRequest request,
            CancellationToken cancellationToken)
        {
            string? cursor = request.Params?["cursor"]?.GetValue<string>();
            var page = await catalog.ListAsync(cursor, cancellationToken).ConfigureAwait(false);

            var result = new ListSkillsResult
            {
                Skills = [.. page.Skills],
                NextCursor = page.NextCursor,
                ResultType = "complete",
            };

            if (ShouldEmitCacheAttributes(request))
            {
                result.TimeToLive = skillsOptions.TimeToLive;
                result.CacheScope = skillsOptions.CacheScope;
            }

            return JsonSerializer.SerializeToNode(result, McpSkillsJsonContext.Default.ListSkillsResult);
        }

        private async ValueTask<JsonNode?> HandleGetSkillAsync(
            JsonRpcRequest request,
            CancellationToken cancellationToken)
        {
            string? uri = request.Params?["uri"]?.GetValue<string>();
            if (string.IsNullOrEmpty(uri))
            {
                throw new McpProtocolException("'uri' is required.", McpErrorCode.InvalidParams);
            }

            var skill = await catalog.GetAsync(uri!, cancellationToken).ConfigureAwait(false);
            if (skill is null)
            {
                throw new McpProtocolException($"Unknown skill '{uri}'.", McpErrorCode.InvalidParams);
            }

            var result = new GetSkillResult
            {
                Skill = skill,
                ResultType = "complete",
            };

            return JsonSerializer.SerializeToNode(result, McpSkillsJsonContext.Default.GetSkillResult);
        }

        private bool ShouldEmitCacheAttributes(JsonRpcRequest request)
        {
            if (!skillsOptions.GateCacheAttributesByProtocolVersion)
            {
                return true;
            }

            return McpProtocolVersions.IsJuly2026OrLaterProtocolVersion(request.Context?.ProtocolVersion);
        }
    }
}
