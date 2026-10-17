using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ModelContextProtocol.Extensions.Skills;

/// <summary>
/// Extension methods for <see cref="McpClient"/> to discover skills and verify their file manifests under the
/// MCP Skills extension (SEP-2640).
/// </summary>
/// <remarks>
/// These methods validate entries and verify file content against manifests. They do not verify a skill's
/// frontmatter, which a host must still do before loading it; see <see cref="SkillVerifier"/>.
/// </remarks>
public static class McpSkillsClientExtensions
{
    /// <summary>
    /// Gets whether the server declared the MCP Skills extension in its capabilities.
    /// </summary>
    /// <param name="client">The client.</param>
    /// <returns><see langword="true"/> if the server serves <c>skills/list</c> and <c>skills/get</c>; otherwise, <see langword="false"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="client"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// Clients must issue <c>skills/list</c> and <c>skills/get</c> only after observing the server's declaration.
    /// The other methods in this class throw <see cref="InvalidOperationException"/> when it is absent.
    /// </remarks>
    public static bool SupportsSkills(this McpClient client)
    {
#if NET
        ArgumentNullException.ThrowIfNull(client);
#else
        if (client is null) throw new ArgumentNullException(nameof(client));
#endif

        return client.ServerCapabilities?.Extensions?.ContainsKey(SkillsProtocol.ExtensionId) == true;
    }

    /// <summary>
    /// Retrieves the listed skills, following at most 100 pages and collecting at most 1,000 entries.
    /// </summary>
    /// <param name="client">The client.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests. The default is <see cref="CancellationToken.None"/>.</param>
    /// <returns>The listed skills.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="client"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">The server did not declare the MCP Skills extension.</exception>
    /// <exception cref="SkillVerificationException">
    /// The server returned an entry that violates the specification (for example, a name that does not match its
    /// URI, a malformed digest, or a manifest over the per-skill limits). Hosts must not load such entries.
    /// </exception>
    /// <exception cref="McpException">
    /// The request failed, a pagination cursor repeated, or the listing exceeded the page or entry limit.
    /// </exception>
    /// <remarks>
    /// <para>
    /// The listing may be empty or partial: a server whose catalog is large or generated may list fewer skills than
    /// it serves. Do not treat an empty listing as proof that a server has no skills; a skill's entry can always be
    /// retrieved by URI with <see cref="GetSkillAsync(McpClient, string, CancellationToken)"/>.
    /// </para>
    /// <para>
    /// This overload aggregates every page and does not surface the per-result caching hints. To read those, use
    /// <see cref="ListSkillsAsync(McpClient, ListSkillsRequestParams, CancellationToken)"/>, which returns one page at a time.
    /// Use <see cref="ListSkillsAsync(McpClient, int, int, CancellationToken)"/> to choose different limits.
    /// Exceeding a limit fails the operation rather than returning a silently truncated listing.
    /// </para>
    /// </remarks>
    public static ValueTask<IList<Skill>> ListSkillsAsync(this McpClient client, CancellationToken cancellationToken = default) =>
        ListSkillsAsync(client, maxPages: 100, maxSkills: 1000, cancellationToken);

    /// <summary>
    /// Retrieves the listed skills with explicit page and entry limits.
    /// </summary>
    /// <param name="client">The client.</param>
    /// <param name="maxPages">The maximum number of pages to request. Must be positive.</param>
    /// <param name="maxSkills">The maximum number of entries to collect. Must be positive.</param>
    /// <param name="cancellationToken">The token used to cancel the operation.</param>
    /// <returns>The complete listing within the supplied limits.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="client"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A limit is not positive.</exception>
    /// <exception cref="InvalidOperationException">The server did not declare the Skills extension.</exception>
    /// <exception cref="SkillVerificationException">An entry is invalid.</exception>
    /// <exception cref="McpException">The request failed, a cursor repeated, or a limit was exceeded.</exception>
    /// <remarks>
    /// Cursors are opaque, including an empty string. Only a null cursor ends the listing.
    /// Use the paged overload when the application needs to stream a larger listing.
    /// </remarks>
    public static async ValueTask<IList<Skill>> ListSkillsAsync(
        this McpClient client,
        int maxPages,
        int maxSkills,
        CancellationToken cancellationToken = default)
    {
#if NET
        ArgumentNullException.ThrowIfNull(client);
#else
        if (client is null) throw new ArgumentNullException(nameof(client));
#endif
        if (maxPages <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxPages), "The page limit must be positive.");
        }
        if (maxSkills <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxSkills), "The skill limit must be positive.");
        }

        var skills = new List<Skill>();
        var requestParams = new ListSkillsRequestParams();
        var cursors = new HashSet<string>(StringComparer.Ordinal);
        for (int pageNumber = 0; pageNumber < maxPages; pageNumber++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var page = await ListSkillsAsync(client, requestParams, cancellationToken).ConfigureAwait(false);
            if (page.Skills.Count > maxSkills - skills.Count)
            {
                throw new McpException($"The skill listing exceeded the limit of {maxSkills} entries. Use the paged overload or a larger explicit limit.");
            }
            skills.AddRange(page.Skills);
            if (page.NextCursor is null)
            {
                return skills;
            }
            if (!cursors.Add(page.NextCursor))
            {
                throw new McpException("The server repeated a skills/list pagination cursor.");
            }
            requestParams.Cursor = page.NextCursor;
        }

        throw new McpException($"The skill listing exceeded the limit of {maxPages} pages. Use the paged overload or a larger explicit limit.");
    }

    /// <summary>
    /// Retrieves one page of the skills the server lists.
    /// </summary>
    /// <param name="client">The client.</param>
    /// <param name="requestParams">The request parameters, including the cursor of the page to retrieve.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests. The default is <see cref="CancellationToken.None"/>.</param>
    /// <returns>The page with every entry structurally validated, but not compared with the skill's files.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="client"/> or <paramref name="requestParams"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">The server did not declare the MCP Skills extension.</exception>
    /// <exception cref="SkillVerificationException">The server returned an entry that violates the specification.</exception>
    /// <exception cref="McpException">The request failed or the server returned an error response.</exception>
    public static async ValueTask<ListSkillsResult> ListSkillsAsync(
        this McpClient client,
        ListSkillsRequestParams requestParams,
        CancellationToken cancellationToken = default)
    {
#if NET
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(requestParams);
#else
        if (client is null) throw new ArgumentNullException(nameof(client));
        if (requestParams is null) throw new ArgumentNullException(nameof(requestParams));
#endif

        ThrowIfSkillsNotSupported(client, nameof(ListSkillsAsync));

        JsonRpcRequest request = new()
        {
            Method = SkillsProtocol.MethodSkillsList,
            Params = JsonSerializer.SerializeToNode(requestParams, McpSkillsJsonContext.Default.ListSkillsRequestParams),
        };

        JsonRpcResponse response = await client.SendRequestAsync(request, cancellationToken).ConfigureAwait(false);
        var result = response.Result?.Deserialize(McpSkillsJsonContext.Default.ListSkillsResult) ??
            throw new JsonException($"Unexpected JSON result in the response to '{SkillsProtocol.MethodSkillsList}'.");
        if (result.Skills is null)
        {
            throw new JsonException($"The response to '{SkillsProtocol.MethodSkillsList}' has no 'skills' array.");
        }

        foreach (var skill in result.Skills)
        {
            SkillValidation.ValidateReceived(skill, SkillsProtocol.MethodSkillsList);
        }

        return result;
    }

    /// <summary>
    /// Retrieves a page's valid entries and reports each rejected entry without discarding the rest of the page.
    /// </summary>
    /// <param name="client">The client.</param>
    /// <param name="requestParams">The request parameters, including the page cursor.</param>
    /// <param name="cancellationToken">The token used to cancel the operation.</param>
    /// <returns>The valid entries, original pagination/cache hints, and explicit per-entry failures.</returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">The server did not declare the Skills extension.</exception>
    /// <exception cref="JsonException">The page envelope is malformed or lacks a skills array.</exception>
    /// <exception cref="McpException">The request failed or the server returned an error.</exception>
    /// <remarks>
    /// This is an explicit partial-results API. The ordinary list overloads remain strict.
    /// Callers must inspect <see cref="SkillListingResult.RejectedEntries"/>; an empty valid-entry list can
    /// mean that every entry was rejected. Valid entries still require normal file/frontmatter verification.
    /// </remarks>
    public static async ValueTask<SkillListingResult> ListSkillsWithDiagnosticsAsync(
        this McpClient client,
        ListSkillsRequestParams requestParams,
        CancellationToken cancellationToken = default)
    {
#if NET
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(requestParams);
#else
        if (client is null) throw new ArgumentNullException(nameof(client));
        if (requestParams is null) throw new ArgumentNullException(nameof(requestParams));
#endif
        ThrowIfSkillsNotSupported(client, nameof(ListSkillsWithDiagnosticsAsync));

        var request = new JsonRpcRequest
        {
            Method = SkillsProtocol.MethodSkillsList,
            Params = JsonSerializer.SerializeToNode(requestParams, McpSkillsJsonContext.Default.ListSkillsRequestParams),
        };
        var response = await client.SendRequestAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.Result is not JsonObject rawResult ||
            rawResult["skills"] is not JsonArray entries)
        {
            throw new JsonException($"The response to '{SkillsProtocol.MethodSkillsList}' has no 'skills' array.");
        }

        var envelope = new JsonObject();
        foreach (var property in rawResult)
        {
            envelope[property.Key] = property.Key == "skills" ? new JsonArray() : property.Value?.DeepClone();
        }
        var page = envelope.Deserialize(McpSkillsJsonContext.Default.ListSkillsResult) ??
            throw new JsonException($"Unexpected JSON result in the response to '{SkillsProtocol.MethodSkillsList}'.");
        var rejected = new List<SkillEntryRejection>();
        for (int index = 0; index < entries.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var skill = entries[index]?.Deserialize(McpSkillsJsonContext.Default.Skill);
                SkillValidation.ValidateReceived(skill!, SkillsProtocol.MethodSkillsList);
                page.Skills.Add(skill!);
            }
            catch (Exception error) when (error is JsonException or SkillVerificationException)
            {
                rejected.Add(new SkillEntryRejection(index, error));
            }
        }

        return new SkillListingResult(page, rejected.AsReadOnly());
    }

    /// <summary>
    /// Retrieves the entry for a single skill by the URI of its <c>SKILL.md</c>.
    /// </summary>
    /// <param name="client">The client.</param>
    /// <param name="uri">The URI of the skill's <c>SKILL.md</c>.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests. The default is <see cref="CancellationToken.None"/>.</param>
    /// <returns>The structurally validated skill entry, not a verification of the file's frontmatter.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="client"/> or <paramref name="uri"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">The server did not declare the MCP Skills extension.</exception>
    /// <exception cref="SkillVerificationException">
    /// The server returned an entry that violates the specification, or an entry for a different URI than the one requested.
    /// </exception>
    /// <exception cref="McpException">
    /// The request failed or the server returned an error response, including <see cref="McpErrorCode.InvalidParams"/>
    /// when the server serves no skill at <paramref name="uri"/>.
    /// </exception>
    /// <remarks>
    /// A server answers for every skill it serves, whether or not the skill appears in its listing. Use this to
    /// confirm a URI referenced from server instructions or another skill, and to refresh one skill's manifest
    /// without re-enumerating the catalog.
    /// </remarks>
    public static async ValueTask<Skill> GetSkillAsync(this McpClient client, string uri, CancellationToken cancellationToken = default)
    {
#if NET
        ArgumentNullException.ThrowIfNull(uri);
#else
        if (uri is null) throw new ArgumentNullException(nameof(uri));
#endif

        var result = await GetSkillAsync(client, new GetSkillRequestParams { Uri = uri }, cancellationToken).ConfigureAwait(false);
        return result.Skill;
    }

    /// <summary>
    /// Retrieves the entry for a single skill using explicit request parameters.
    /// </summary>
    /// <param name="client">The client.</param>
    /// <param name="requestParams">The request parameters.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests. The default is <see cref="CancellationToken.None"/>.</param>
    /// <returns>The result with its entry structurally validated, but not compared with the skill's files.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="client"/> or <paramref name="requestParams"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">The server did not declare the MCP Skills extension.</exception>
    /// <exception cref="SkillVerificationException">
    /// The server returned an entry that violates the specification, or an entry for a different URI than the one requested.
    /// </exception>
    /// <exception cref="McpException">The request failed or the server returned an error response.</exception>
    public static async ValueTask<GetSkillResult> GetSkillAsync(
        this McpClient client,
        GetSkillRequestParams requestParams,
        CancellationToken cancellationToken = default)
    {
#if NET
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(requestParams);
#else
        if (client is null) throw new ArgumentNullException(nameof(client));
        if (requestParams is null) throw new ArgumentNullException(nameof(requestParams));
#endif

        ThrowIfSkillsNotSupported(client, nameof(GetSkillAsync));

        JsonRpcRequest request = new()
        {
            Method = SkillsProtocol.MethodSkillsGet,
            Params = JsonSerializer.SerializeToNode(requestParams, McpSkillsJsonContext.Default.GetSkillRequestParams),
        };

        JsonRpcResponse response = await client.SendRequestAsync(request, cancellationToken).ConfigureAwait(false);
        var result = response.Result?.Deserialize(McpSkillsJsonContext.Default.GetSkillResult) ??
            throw new JsonException($"Unexpected JSON result in the response to '{SkillsProtocol.MethodSkillsGet}'.");

        SkillValidation.ValidateReceived(result.Skill, SkillsProtocol.MethodSkillsGet);
        if (!string.Equals(result.Skill.Uri, requestParams.Uri, StringComparison.Ordinal))
        {
            throw new SkillVerificationException(
                $"The server answered '{SkillsProtocol.MethodSkillsGet}' for '{requestParams.Uri}' with the entry for '{result.Skill.Uri}'.");
        }

        return result;
    }

    /// <summary>
    /// Reads one of a skill's files through <c>resources/read</c> and verifies its URI, digest and size against the manifest.
    /// </summary>
    /// <param name="client">The client.</param>
    /// <param name="skill">The skill entry being acted on.</param>
    /// <param name="uri">The URI of the file to read. It must be listed in <paramref name="skill"/>'s manifest.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests. The default is <see cref="CancellationToken.None"/>.</param>
    /// <returns>The manifest-verified contents. Frontmatter verification remains the host's responsibility.</returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">
    /// <paramref name="skill"/>'s manifest is <see cref="SkillResources.Dynamic"/>. Such a skill offers no content
    /// integrity; read its files with <see cref="McpClient.ReadResourceAsync(string, RequestOptions?, CancellationToken)"/>
    /// only if the host permits content without digest verification. Frontmatter verification is still required.
    /// </exception>
    /// <exception cref="SkillVerificationException">
    /// <paramref name="uri"/> is not listed in the manifest, the server's response does not contain contents for
    /// <paramref name="uri"/>, or any returned content's size or digest does not match its manifest entry.
    /// </exception>
    /// <exception cref="McpException">The request failed or the server returned an error response.</exception>
    /// <remarks>
    /// <para>
    /// An unlisted file is treated as a verification failure before any request is sent, because the manifest is
    /// complete and an unlisted file is a change to the skill. To read it, refresh the entry with
    /// <see cref="GetSkillAsync(McpClient, string, CancellationToken)"/> and proceed from the new manifest.
    /// </para>
    /// <para>
    /// For <c>SKILL.md</c>, the host must also parse the fetched frontmatter and compare it with
    /// <see cref="Skill.Frontmatter"/> before loading. A digest match does not perform or replace that check.
    /// </para>
    /// </remarks>
    public static async ValueTask<ReadResourceResult> ReadSkillResourceAsync(
        this McpClient client,
        Skill skill,
        string uri,
        CancellationToken cancellationToken = default)
    {
#if NET
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(skill);
        ArgumentNullException.ThrowIfNull(uri);
#else
        if (client is null) throw new ArgumentNullException(nameof(client));
        if (skill is null) throw new ArgumentNullException(nameof(skill));
        if (uri is null) throw new ArgumentNullException(nameof(uri));
#endif

        SkillVerifier.ThrowIfDynamic(skill);
        if (SkillVerifier.FindResource(skill, uri) is null)
        {
            throw new SkillVerificationException(
                $"'{uri}' is not listed in the manifest of skill '{skill.Uri}'. An unlisted file is a change to the skill; " +
                "refresh the entry with skills/get before reading it.");
        }

        var result = await client.ReadResourceAsync(uri, cancellationToken: cancellationToken).ConfigureAwait(false);
        SkillVerifier.Verify(skill, uri, result);
        return result;
    }

    private static void ThrowIfSkillsNotSupported(McpClient client, string operationName)
    {
        if (!SupportsSkills(client))
        {
            throw new InvalidOperationException(
                $"'{operationName}' requires the server to declare the '{SkillsProtocol.ExtensionId}' extension in its capabilities, and it did not.");
        }
    }
}
