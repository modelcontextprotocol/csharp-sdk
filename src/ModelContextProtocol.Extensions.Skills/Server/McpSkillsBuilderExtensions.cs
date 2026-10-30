using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;

namespace ModelContextProtocol.Extensions.Skills;

/// <summary>Extension methods that enable MCP Skills (SEP-2640) support.</summary>
public static class McpSkillsBuilderExtensions
{
    /// <summary>Enables Skills with registered primitives and optional custom request handlers.</summary>
    /// <param name="builder">The server builder.</param>
    /// <param name="configure">An optional callback configuring the collection, handlers, and cache defaults.</param>
    /// <returns>The supplied builder.</returns>
    /// <remarks>
    /// <para>
    /// Calls contribute to the same <see cref="McpSkillsOptions"/>, configured in registration order. Each wire
    /// handler is installed once. A later callback can replace or clear an earlier handler; independent cursors
    /// are never composed automatically. The default handlers list and retrieve <see cref="McpSkillsOptions.SkillCollection"/>.
    /// A custom handler replaces its operation completely; it neither augments the default listing nor falls back
    /// to registered entries. The SDK still validates and serializes its result.
    /// </para>
    /// <para>
    /// Files authored by the configured primitives are registered as ordinary resources when server options are built. Shared files
    /// must have identical URI text, digest, and size; conflicting application resources fail options construction.
    /// Later changes to the skill collection affect entries only. Applications manage later resource additions and
    /// removals separately, including resource conflicts. Removing a skill does not revoke its files.
    /// </para>
    /// <para>
    /// Handlers receive the existing request's services and principal. No provider or additional scope is created
    /// by Skills. Options-only <see cref="McpServer.Create"/> works without services when handlers need none.
    /// Skills requests do not pass through resource request filters; protect metadata and files with the same
    /// application policy. Server-local primitive metadata is not interpreted as authorization.
    /// </para>
    /// </remarks>
    public static IMcpServerBuilder WithSkills(this IMcpServerBuilder builder, Action<McpSkillsOptions>? configure = null)
    {
#if NET
        ArgumentNullException.ThrowIfNull(builder);
#else
        if (builder is null) throw new ArgumentNullException(nameof(builder));
#endif

        builder.Services.AddOptions<McpSkillsOptions>();
        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IConfigureOptions<McpSkillsOptions>, McpSkillsOptionsSetup>());
        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IConfigureOptions<McpServerOptions>, McpSkillsConfigureOptions>());
        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IPostConfigureOptions<McpServerOptions>, SkillFileRegistration>());
        if (configure is not null)
        {
            builder.Services.Configure(configure);
        }
        return builder;
    }

    /// <summary>Enables Skills with the supplied server-side primitives and their authored file resources.</summary>
    /// <param name="builder">The server builder.</param>
    /// <param name="skills">The primitives to add to the default collection.</param>
    /// <param name="configure">An optional callback configuring the collection, handlers, and cache defaults.</param>
    /// <returns>The supplied builder.</returns>
    /// <remarks>
    /// Repeated calls add primitives to the same collection. Duplicate identities and file conflicts fail when
    /// options are built; they are not silently replaced. Registration and handler composition follow
    /// <see cref="WithSkills(IMcpServerBuilder, Action{McpSkillsOptions})"/>.
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

        foreach (var skill in skills)
        {
            if (skill is null)
            {
                throw new ArgumentException("The skills must not contain null entries.", nameof(skills));
            }
            builder.Services.AddSingleton(skill);
        }
        return WithSkills(builder, configure);
    }

    /// <summary>Enables Skills from the immediate skill subdirectories, excluding hidden and system entries.</summary>
    /// <param name="builder">The server builder.</param>
    /// <param name="directoryPath">A directory whose immediate skill subdirectories contain <c>SKILL.md</c>.</param>
    /// <param name="uriPrefix">The URI prefix, defaulting to <c>skill://</c>.</param>
    /// <param name="configure">An optional callback configuring the extension.</param>
    /// <returns>The supplied builder.</returns>
    /// <remarks>
    /// Files are read once and snapshotted. Invalid skills and symbolic links are errors, not skipped entries.
    /// Registration and file-conflict behavior follow <see cref="WithSkills(IMcpServerBuilder, IEnumerable{McpServerSkill}, Action{McpSkillsOptions})"/>.
    /// </remarks>
    public static IMcpServerBuilder WithSkillsFromDirectory(
        this IMcpServerBuilder builder,
        string directoryPath,
        string uriPrefix = "skill://",
        Action<McpSkillsOptions>? configure = null) =>
        WithSkillsFromDirectory(builder, directoryPath, includeHiddenAndSystemFiles: false, uriPrefix, configure);

    /// <summary>Enables Skills from immediate subdirectories with an explicit hidden/system-file policy.</summary>
    /// <param name="builder">The server builder.</param>
    /// <param name="directoryPath">The directory containing skill directories.</param>
    /// <param name="includeHiddenAndSystemFiles">Whether to include dot-prefixed, hidden, and system entries.</param>
    /// <param name="uriPrefix">The prefix used to derive skill URIs.</param>
    /// <param name="configure">An optional callback configuring the extension.</param>
    /// <returns>The supplied builder.</returns>
    /// <remarks>Included symbolic links remain errors. See <see cref="WithSkillsFromDirectory(IMcpServerBuilder, string, string, Action{McpSkillsOptions})"/>.</remarks>
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
            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new ArgumentException(
                    $"'{skillDirectory}' is a symbolic link or other reparse point. Links are not followed when loading skills, " +
                    "because a link can point outside the skills directory. Replace it with a regular directory.",
                    nameof(directoryPath));
            }
            if (File.Exists(Path.Combine(skillDirectory, SkillsProtocol.SkillFileName)))
            {
                skills.Add(McpServerSkill.CreateFromDirectory(
                    skillDirectory, uriPrefix, nameof(directoryPath), includeHiddenAndSystemFiles));
            }
        }
        if (skills.Count == 0)
        {
            throw new ArgumentException(
                $"No skill directories were found under '{fullDirectory}'. Each skill must be an immediate subdirectory containing a {SkillsProtocol.SkillFileName}.",
                nameof(directoryPath));
        }
        return WithSkills(builder, skills, configure);
    }

    private sealed class McpSkillsOptionsSetup(IEnumerable<McpServerSkill> skills) : IConfigureOptions<McpSkillsOptions>
    {
        public void Configure(McpSkillsOptions options)
        {
            foreach (var skill in skills)
            {
                options.SkillCollection.Add(skill);
            }
        }
    }

    private sealed class SkillFileRegistration(IOptions<McpSkillsOptions> skillsOptions, IEnumerable<McpServerResource> registeredResources)
        : IPostConfigureOptions<McpServerOptions>
    {
        public void PostConfigure(string? name, McpServerOptions options)
        {
            var authoredFiles = skillsOptions.Value.SkillCollection.SelectMany(skill => skill.Resources).ToArray();
            var skillFiles = new McpServerResourceCollection();
            foreach (var file in authoredFiles)
            {
                skillFiles.TryAdd(file);
            }
            var resources = options.ResourceCollection is { } collection ?
                registeredResources.Concat(collection) : registeredResources;
            foreach (var resource in resources)
            {
                string uri = resource.ProtocolResourceTemplate.UriTemplate;
                if (skillFiles.TryGetPrimitive(uri, out var file) &&
                    !authoredFiles.Any(authored => ReferenceEquals(authored, resource)))
                {
                    throw new InvalidOperationException(
                        $"The Skills file '{file.ProtocolResourceTemplate.UriTemplate}' conflicts with another resource registered at '{uri}'. " +
                        "Remove the other resource, or give it a different URI.");
                }
            }
            if (!skillFiles.IsEmpty)
            {
                options.ResourceCollection ??= new();
                foreach (var file in skillFiles)
                {
                    options.ResourceCollection.TryAdd(file);
                }
            }
        }
    }

    private sealed class McpSkillsConfigureOptions(IOptions<McpSkillsOptions> options) : IConfigureOptions<McpServerOptions>
    {
        private readonly McpSkillsOptions _skillsOptions = options.Value;
        private static readonly UTF8Encoding s_strictUtf8 = new(false, true);

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
            var parameters = DeserializeParams(context, McpSkillsJsonContext.Default.ListSkillsRequestParams) ?? new();
            var typedContext = new RequestContext<ListSkillsRequestParams>(context.Server, context.JsonRpcRequest, parameters)
            {
                Services = context.Services,
            };
            var handler = _skillsOptions.ListSkillsHandler;
            var page = handler is not null ?
                await handler(typedContext, cancellationToken).ConfigureAwait(false) :
                ListRegisteredSkills(parameters);
            if (page?.Skills is null)
            {
                throw new McpProtocolException("The Skills list handler returned no page or skills collection.", McpErrorCode.InternalError);
            }

            IList<Skill> entries = page.Skills;
            if (handler is not null)
            {
                entries = new List<Skill>(page.Skills.Count);
                var seen = new HashSet<string>(StringComparer.Ordinal);
                var responseFiles = new Dictionary<Uri, SkillResource>();
                foreach (var entry in page.Skills)
                {
                    var snapshot = ValidateAndSnapshotEntry(entry, responseFiles);
                    if (!seen.Add(snapshot.Uri))
                    {
                        throw new McpProtocolException($"The Skills list handler returned duplicate skill URI '{snapshot.Uri}'.", McpErrorCode.InternalError);
                    }
                    entries.Add(snapshot);
                }
            }
            bool modern = IsJuly2026OrLaterProtocolRequest(context.JsonRpcRequest);
            var result = new ListSkillsResult
            {
                Skills = entries,
                NextCursor = page.NextCursor,
                Meta = page.Meta?.DeepClone().AsObject(),
                ResultType = modern ? "complete" : null,
                TimeToLive = modern ? page.TimeToLive ?? _skillsOptions.TimeToLive ?? TimeSpan.Zero : null,
                CacheScope = modern ? page.CacheScope ?? _skillsOptions.CacheScope ?? CacheScope.Private : null,
            };
            return JsonSerializer.SerializeToNode(result, McpSkillsJsonContext.Default.ListSkillsResult);
        }

        private async ValueTask<JsonNode?> HandleGetSkillAsync(RequestContext<JsonNode?> context, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var parameters = DeserializeParams(context, McpSkillsJsonContext.Default.GetSkillRequestParams);
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
            var handler = _skillsOptions.GetSkillHandler;
            var response = handler is not null ?
                await handler(typedContext, cancellationToken).ConfigureAwait(false) :
                new GetSkillResult
                {
                    Skill = primitive?.ProtocolSkill ??
                        throw new McpProtocolException($"No skill is served at '{uri}'.", McpErrorCode.InvalidParams),
                };
            var entry = handler is null ? response.Skill : ValidateAndSnapshotEntry(response?.Skill);
            if (!string.Equals(entry.Uri, uri, StringComparison.Ordinal))
            {
                throw new McpProtocolException(
                    $"The Skills get handler answered a request for '{uri}' with the entry for '{entry.Uri}'.",
                    McpErrorCode.InternalError);
            }
            bool modern = IsJuly2026OrLaterProtocolRequest(context.JsonRpcRequest);
            var result = new GetSkillResult
            {
                Skill = entry,
                Meta = response!.Meta?.DeepClone().AsObject(),
                ResultType = modern ? "complete" : null,
                TimeToLive = modern ? response.TimeToLive ?? _skillsOptions.TimeToLive ?? TimeSpan.Zero : null,
                CacheScope = modern ? response.CacheScope ?? _skillsOptions.CacheScope ?? CacheScope.Private : null,
            };
            return JsonSerializer.SerializeToNode(result, McpSkillsJsonContext.Default.GetSkillResult);
        }

        private ListSkillsResult ListRegisteredSkills(ListSkillsRequestParams parameters)
        {
            var ordered = _skillsOptions.GetSortedSkills();
            string? afterUri = DecodeCursor(parameters.Cursor);
            int start = 0;
            while (start < ordered.Length && afterUri is not null && string.CompareOrdinal(ordered[start].Id, afterUri) <= 0)
            {
                start++;
            }
            int count = Math.Min(_skillsOptions.PageSize, ordered.Length - start);
            var entries = new List<Skill>(count);
            for (int i = 0; i < count; i++)
            {
                entries.Add(ordered[start + i].ProtocolSkill);
            }
            return new ListSkillsResult
            {
                Skills = entries,
                NextCursor = start + count < ordered.Length ?
                    Convert.ToBase64String(Encoding.UTF8.GetBytes(ordered[start + count - 1].Id)) : null,
            };
        }

        private static Skill ValidateAndSnapshotEntry(Skill? entry, Dictionary<Uri, SkillResource>? responseFiles = null)
        {
            Skill snapshot;
            try
            {
                SkillValidation.Validate(entry!, "entry");
                snapshot = SkillValidation.Snapshot(entry!);
                SkillValidation.Validate(snapshot, "entry");
                if (responseFiles is not null)
                {
                    McpServerSkillCollection.AddFiles(responseFiles, snapshot);
                }
            }
            catch (ArgumentException e)
            {
                throw new McpProtocolException($"The Skills handler returned an invalid entry: {e.Message}", McpErrorCode.InternalError);
            }

            return snapshot;
        }

        private static string? DecodeCursor(string? cursor)
        {
            if (string.IsNullOrEmpty(cursor))
            {
                return null;
            }
            try
            {
                return s_strictUtf8.GetString(Convert.FromBase64String(cursor!));
            }
            catch (Exception exception) when (exception is FormatException or DecoderFallbackException)
            {
                throw new McpProtocolException($"Invalid cursor '{cursor}'.", McpErrorCode.InvalidParams);
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

        private static bool IsJuly2026OrLaterProtocolRequest(JsonRpcRequest request) =>
            McpProtocolVersions.IsJuly2026OrLaterProtocolVersion(request.Context?.ProtocolVersion);
    }
}
