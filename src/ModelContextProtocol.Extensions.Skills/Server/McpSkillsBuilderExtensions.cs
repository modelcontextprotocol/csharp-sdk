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
    /// provided every skill lists it with the same digest.
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
        var registeredFiles = new Dictionary<string, string>(StringComparer.Ordinal);
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
                if (registeredFiles.TryGetValue(entry.Uri, out string? existingDigest))
                {
                    if (!string.Equals(existingDigest, entry.Digest, StringComparison.Ordinal))
                    {
                        throw new ArgumentException(
                            $"The file '{entry.Uri}' is listed by more than one skill with different content.",
                            nameof(skills));
                    }

                    continue;
                }

                registeredFiles.Add(entry.Uri, entry.Digest);
                builder.Services.AddSingleton(skill.Resources[i]);
            }
        }

        return WithSkills(builder, new InMemoryMcpSkillCatalog(entries), configure);
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
            var page = await catalog.ListAsync(requestParams?.Cursor, cancellationToken).ConfigureAwait(false);

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

            var skill = await catalog.GetAsync(requestParams!.Uri, cancellationToken).ConfigureAwait(false) ??
                throw new McpProtocolException($"No skill is served at '{requestParams.Uri}'.", McpErrorCode.InvalidParams);

            var result = new GetSkillResult { Skill = skill };
            if (IsJuly2026OrLaterProtocolRequest(request))
            {
                result.ResultType = "complete";
            }

            return JsonSerializer.SerializeToNode(result, McpSkillsJsonContext.Default.GetSkillResult);
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
