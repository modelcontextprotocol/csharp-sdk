using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using System.Text.Json;

namespace ModelContextProtocol.Extensions.Skills;

/// <summary>
/// Extension methods for <see cref="McpClient"/> to discover, retrieve, and verify skills served under the
/// MCP Skills extension (SEP-2640).
/// </summary>
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
    /// Retrieves the entries of every skill the server lists, following pagination to the end.
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
    /// <exception cref="McpException">The request failed or the server returned an error response.</exception>
    /// <remarks>
    /// <para>
    /// The listing may be empty or partial: a server whose catalog is large or generated may list fewer skills than
    /// it serves. Do not treat an empty listing as proof that a server has no skills; a skill's entry can always be
    /// retrieved by URI with <see cref="GetSkillAsync(McpClient, string, CancellationToken)"/>.
    /// </para>
    /// <para>
    /// This overload aggregates every page and does not surface the per-result caching hints. To read those, use
    /// <see cref="ListSkillsAsync(McpClient, ListSkillsRequestParams, CancellationToken)"/>, which returns one page at a time.
    /// </para>
    /// </remarks>
    public static async ValueTask<IList<Skill>> ListSkillsAsync(this McpClient client, CancellationToken cancellationToken = default)
    {
        List<Skill>? skills = null;
        ListSkillsRequestParams requestParams = new();
        do
        {
            var page = await ListSkillsAsync(client, requestParams, cancellationToken).ConfigureAwait(false);
            skills ??= new(page.Skills.Count);
            skills.AddRange(page.Skills);
            requestParams.Cursor = page.NextCursor;
        }
        while (requestParams.Cursor is not null);

        return skills;
    }

    /// <summary>
    /// Retrieves one page of the skills the server lists.
    /// </summary>
    /// <param name="client">The client.</param>
    /// <param name="requestParams">The request parameters, including the cursor of the page to retrieve.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests. The default is <see cref="CancellationToken.None"/>.</param>
    /// <returns>The page, as returned by the server, with every entry validated against the specification.</returns>
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

        foreach (var skill in result.Skills)
        {
            SkillValidation.ValidateReceived(skill, SkillsProtocol.MethodSkillsList);
        }

        return result;
    }

    /// <summary>
    /// Retrieves the entry for a single skill by the URI of its <c>SKILL.md</c>.
    /// </summary>
    /// <param name="client">The client.</param>
    /// <param name="uri">The URI of the skill's <c>SKILL.md</c>.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests. The default is <see cref="CancellationToken.None"/>.</param>
    /// <returns>The skill's entry, validated against the specification.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="client"/> or <paramref name="uri"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">The server did not declare the MCP Skills extension.</exception>
    /// <exception cref="SkillVerificationException">The server returned an entry that violates the specification.</exception>
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
    /// <returns>The result, as returned by the server.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="client"/> or <paramref name="requestParams"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">The server did not declare the MCP Skills extension.</exception>
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
        return result;
    }

    /// <summary>
    /// Reads one of a skill's files through <c>resources/read</c> and verifies the content against the skill's manifest.
    /// </summary>
    /// <param name="client">The client.</param>
    /// <param name="skill">The skill entry being acted on.</param>
    /// <param name="uri">The URI of the file to read. It must be listed in <paramref name="skill"/>'s manifest.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests. The default is <see cref="CancellationToken.None"/>.</param>
    /// <returns>The verified contents.</returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">
    /// <paramref name="skill"/>'s manifest is <see cref="SkillResources.Dynamic"/>. Such a skill offers no content
    /// integrity; read its files with <see cref="McpClient.ReadResourceAsync(string, RequestOptions?, CancellationToken)"/>
    /// only if the host has decided to load unverifiable skills.
    /// </exception>
    /// <exception cref="SkillVerificationException">
    /// <paramref name="uri"/> is not listed in the manifest, the server's response does not contain contents for
    /// <paramref name="uri"/>, or any returned content's size or digest does not match its manifest entry.
    /// </exception>
    /// <exception cref="McpException">The request failed or the server returned an error response.</exception>
    /// <remarks>
    /// An unlisted file is treated as a verification failure before any request is sent, because the manifest is
    /// complete and an unlisted file is a change to the skill. To read it, refresh the entry with
    /// <see cref="GetSkillAsync(McpClient, string, CancellationToken)"/> and proceed from the new manifest.
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

        if (skill.Resources.IsDynamic)
        {
            throw new InvalidOperationException(
                $"Skill '{skill.Uri}' declares dynamic resources, which carry no digests and cannot be verified. " +
                $"Use {nameof(McpClient.ReadResourceAsync)} directly if unverifiable content is acceptable.");
        }

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
