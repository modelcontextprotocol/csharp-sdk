using Microsoft.Extensions.DependencyInjection;
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
    /// <exception cref="InvalidOperationException">Skills are already enabled for this service collection.</exception>
    /// <remarks>
    /// <para>
    /// Call once per server. The default handlers list and retrieve <see cref="McpSkillsOptions.SkillCollection"/>.
    /// A custom handler replaces its operation completely; it neither augments the default listing nor falls back
    /// to registered entries. The SDK still validates and serializes its result.
    /// </para>
    /// <para>
    /// Files authored by the initially registered primitives are registered as ordinary resources. Shared files
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

        ThrowIfSkillsRegistered(builder);
        var options = new McpSkillsOptions();
        configure?.Invoke(options);

        // Stage everything before changing the service collection, so a failed registration can be retried.
        var files = new Dictionary<Uri, McpServerResource?>();
        foreach (var skill in options.SkillCollection)
        {
            var manifest = skill.ProtocolSkill.Resources.Resources;
            if (manifest is null)
            {
                continue;
            }

            for (int i = 0; i < manifest.Count; i++)
            {
                var entry = manifest[i];
                var key = new Uri(entry.Uri, UriKind.Absolute);
                var resource = skill.Resources.Count == 0 ? null : skill.Resources[i];
                if (files.TryGetValue(key, out var existing))
                {
                    if (existing is null && resource is not null)
                    {
                        files[key] = resource;
                    }
                }
                else
                {
                    files.Add(key, resource);
                }
            }
        }

        var skillFiles = new McpServerResourceCollection();
        foreach (var resource in files.Values)
        {
            if (resource is not null)
            {
                skillFiles.Add(resource);
            }
        }

        builder.Services.AddSingleton(new SkillsRegistrationMarker());
        builder.Services.AddSingleton<IConfigureOptions<McpServerOptions>>(new McpSkillsConfigureOptions(options));
        foreach (var resource in skillFiles)
        {
            builder.Services.AddSingleton(resource);
        }
        builder.Services.AddSingleton<IPostConfigureOptions<McpServerOptions>>(
            services => new SkillFileConflictCheck(skillFiles, services.GetServices<McpServerResource>()));
        return builder;
    }

    /// <summary>Enables Skills with the supplied server-side primitives and their authored file resources.</summary>
    /// <param name="builder">The server builder.</param>
    /// <param name="skills">The primitives to add to the default collection.</param>
    /// <param name="configure">An optional callback configuring the collection, handlers, and cache defaults.</param>
    /// <returns>The supplied builder.</returns>
    /// <remarks>Registration, handler composition, and mutation semantics follow <see cref="WithSkills(IMcpServerBuilder, Action{McpSkillsOptions})"/>.</remarks>
    public static IMcpServerBuilder WithSkills(
        this IMcpServerBuilder builder,
        IEnumerable<McpServerSkill> skills,
        Action<McpSkillsOptions>? configure = null)
    {
#if NET
        ArgumentNullException.ThrowIfNull(skills);
#else
        if (skills is null) throw new ArgumentNullException(nameof(skills));
#endif

        return WithSkills(builder, options =>
        {
            foreach (var skill in skills)
            {
                options.SkillCollection.Add(skill);
            }
            configure?.Invoke(options);
        });
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

    private static void ThrowIfSkillsRegistered(IMcpServerBuilder builder)
    {
        if (builder.Services.Any(descriptor => descriptor.ServiceType == typeof(SkillsRegistrationMarker)))
        {
            throw new InvalidOperationException(
                "Skills are already registered for this server. Call WithSkills or WithSkillsFromDirectory once; " +
                "configure one skill collection and one pair of request handlers.");
        }
    }

    private sealed class SkillsRegistrationMarker;

    private sealed class SkillFileConflictCheck(McpServerResourceCollection skillFiles, IEnumerable<McpServerResource> registeredResources)
        : IPostConfigureOptions<McpServerOptions>
    {
        public void PostConfigure(string? name, McpServerOptions options)
        {
            var resources = options.ResourceCollection is { } collection ?
                registeredResources.Concat(collection) : registeredResources;
            foreach (var resource in resources)
            {
                string uri = resource.ProtocolResourceTemplate.UriTemplate;
                if (skillFiles.TryGetPrimitive(uri, out var file) && !ReferenceEquals(file, resource))
                {
                    throw new InvalidOperationException(
                        $"The Skills file '{file.ProtocolResourceTemplate.UriTemplate}' conflicts with another resource registered at '{uri}'. " +
                        "Remove the other resource, or give it a different URI.");
                }
            }
        }
    }

    private sealed class McpSkillsConfigureOptions(McpSkillsOptions skillsOptions) : IConfigureOptions<McpServerOptions>
    {
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
            var handler = skillsOptions.ListSkillsHandler;
            var page = handler is not null ?
                await handler(typedContext, cancellationToken).ConfigureAwait(false) :
                ListRegisteredSkills(parameters);
            if (page?.Skills is null)
            {
                throw new McpProtocolException("The Skills list handler returned no page or skills collection.", McpErrorCode.InternalError);
            }

            var entries = new List<Skill>(page.Skills.Count);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            Dictionary<Uri, SkillResource>? responseFiles = handler is not null ? new() : null;
            foreach (var entry in page.Skills)
            {
                var snapshot = ValidateAndSnapshotEntry(entry, validateSharedFiles: handler is not null, responseFiles);
                if (!seen.Add(snapshot.Uri))
                {
                    throw new McpProtocolException($"The Skills list handler returned duplicate skill URI '{snapshot.Uri}'.", McpErrorCode.InternalError);
                }
                entries.Add(snapshot);
            }
            bool modern = IsJuly2026OrLaterProtocolRequest(context.JsonRpcRequest);
            var result = new ListSkillsResult
            {
                Skills = entries,
                NextCursor = page.NextCursor,
                Meta = page.Meta?.DeepClone().AsObject(),
                ResultType = modern ? "complete" : null,
                TimeToLive = modern ? page.TimeToLive ?? skillsOptions.TimeToLive ?? TimeSpan.Zero : null,
                CacheScope = modern ? page.CacheScope ?? skillsOptions.CacheScope ?? CacheScope.Private : null,
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
            skillsOptions.SkillCollection.TryGetPrimitive(uri, out var primitive);
            var typedContext = new RequestContext<GetSkillRequestParams>(context.Server, context.JsonRpcRequest, parameters)
            {
                Services = context.Services,
                MatchedPrimitive = primitive,
            };
            var handler = skillsOptions.GetSkillHandler;
            var response = handler is not null ?
                await handler(typedContext, cancellationToken).ConfigureAwait(false) :
                new GetSkillResult
                {
                    Skill = primitive?.ProtocolSkill ??
                        throw new McpProtocolException($"No skill is served at '{uri}'.", McpErrorCode.InvalidParams),
                };
            var entry = ValidateAndSnapshotEntry(response?.Skill, validateSharedFiles: handler is not null);
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
                TimeToLive = modern ? response.TimeToLive ?? skillsOptions.TimeToLive ?? TimeSpan.Zero : null,
                CacheScope = modern ? response.CacheScope ?? skillsOptions.CacheScope ?? CacheScope.Private : null,
            };
            return JsonSerializer.SerializeToNode(result, McpSkillsJsonContext.Default.GetSkillResult);
        }

        private ListSkillsResult ListRegisteredSkills(ListSkillsRequestParams parameters)
        {
            var ordered = skillsOptions.SkillCollection.ToArray();
            Array.Sort(ordered, static (left, right) => string.CompareOrdinal(left.Id, right.Id));
            string? afterUri = DecodeCursor(parameters.Cursor);
            int start = 0;
            while (start < ordered.Length && afterUri is not null && string.CompareOrdinal(ordered[start].Id, afterUri) <= 0)
            {
                start++;
            }
            int count = Math.Min(skillsOptions.PageSize, ordered.Length - start);
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

        private Skill ValidateAndSnapshotEntry(Skill? entry, bool validateSharedFiles, Dictionary<Uri, SkillResource>? responseFiles = null)
        {
            Skill snapshot;
            try
            {
                SkillValidation.Validate(entry!, "entry");
                snapshot = SkillValidation.Snapshot(entry!);
                SkillValidation.Validate(snapshot, "entry");
                if (validateSharedFiles)
                {
                    skillsOptions.ValidateSharedFiles(snapshot);
                }
                if (responseFiles is not null)
                {
                    McpServerSkillCollection.AddFiles(responseFiles, snapshot);
                }
            }
            catch (ArgumentException e)
            {
                throw new McpProtocolException($"The Skills handler returned an invalid entry: {e.Message}", McpErrorCode.InternalError);
            }

            if (skillsOptions.SkillCollection.TryGetPrimitive(snapshot.Uri, out var primitive))
            {
                var registered = primitive.ProtocolSkill;
                if (!JsonNode.DeepEquals(registered.Frontmatter, snapshot.Frontmatter) ||
                    !SameManifest(registered.Resources, snapshot.Resources))
                {
                    throw new McpProtocolException(
                        $"The Skills handler returned content that conflicts with registered skill '{snapshot.Uri}'.", McpErrorCode.InternalError);
                }
            }
            return snapshot;
        }

        private static bool SameManifest(SkillResources expected, SkillResources actual)
        {
            if (expected.Resources is not { } expectedFiles)
            {
                return actual.IsDynamic;
            }
            if (actual.Resources is not { } actualFiles || actualFiles.Count != expectedFiles.Count)
            {
                return false;
            }
            var byUri = actualFiles.ToDictionary(file => file.Uri, StringComparer.Ordinal);
            return expectedFiles.All(file => byUri.TryGetValue(file.Uri, out var other) && McpServerSkillCollection.SameFile(file, other));
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
