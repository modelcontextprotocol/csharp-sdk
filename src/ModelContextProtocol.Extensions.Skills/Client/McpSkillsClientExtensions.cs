using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace ModelContextProtocol.Extensions.Skills;

/// <summary>
/// Extension methods for <see cref="McpClient"/> and <see cref="ServerCapabilities"/> to discover skills and
/// verify their file manifests under the MCP Skills extension (SEP-2640).
/// </summary>
/// <remarks>
/// These methods validate entries and verify file content against manifests. They do not verify a skill's
/// frontmatter, which a host must still do before loading it; see <see cref="SkillVerifier"/>.
/// </remarks>
[Experimental(Experimentals.Skills_DiagnosticId, UrlFormat = Experimentals.Skills_Url)]
public static class McpSkillsClientExtensions
{
    /// <summary>
    /// Gets whether the server declared the MCP Skills extension in its capabilities.
    /// </summary>
    /// <param name="capabilities">The server's advertised capabilities.</param>
    /// <returns><see langword="true"/> if the server declared the MCP Skills extension; otherwise, <see langword="false"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="capabilities"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// Clients must issue <c>skills/list</c> and <c>skills/get</c> only after observing the server's declaration.
    /// The list and get methods in this class throw <see cref="InvalidOperationException"/> when it is absent.
    /// </remarks>
    [Experimental(Experimentals.Skills_DiagnosticId, UrlFormat = Experimentals.Skills_Url)]
    public static bool SupportsSkills(this ServerCapabilities capabilities)
    {
#if NET
        ArgumentNullException.ThrowIfNull(capabilities);
#else
        if (capabilities is null) throw new ArgumentNullException(nameof(capabilities));
#endif

        return capabilities.Extensions?.ContainsKey(SkillsProtocol.ExtensionId) == true;
    }

    /// <summary>
    /// Retrieves the listed skills by automatically handling pagination.
    /// </summary>
    /// <param name="client">The client.</param>
    /// <param name="options">Optional metadata and progress token for each request.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests. The default is <see cref="CancellationToken.None"/>.</param>
    /// <returns>The listed skills, with every entry structurally validated but not compared with its files.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="client"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">The server did not declare the MCP Skills extension.</exception>
    /// <exception cref="InvalidDataException">The server returned an invalid skill entry.</exception>
    /// <exception cref="McpException">The request failed or the server returned an error response.</exception>
    /// <remarks>
    /// <para>
    /// A server may list fewer skills than it serves. An empty listing does not prevent direct retrieval by URI
    /// with <see cref="GetSkillAsync(McpClient, string, RequestOptions?, CancellationToken)"/>.
    /// </para>
    /// <para>
    /// Every call fetches all listing pages without caching. Per-page cache hints are not returned; use
    /// <see cref="ListSkillsAsync(McpClient, ListSkillsRequestParams, CancellationToken)"/> to read them or to
    /// enforce application-owned pagination budgets.
    /// </para>
    /// </remarks>
    [Experimental(Experimentals.Skills_DiagnosticId, UrlFormat = Experimentals.Skills_Url)]
    public static async ValueTask<IList<Skill>> ListSkillsAsync(
        this McpClient client,
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        List<Skill>? skills = null;
        ListSkillsRequestParams requestParams = new() { Meta = options?.GetMetaForRequest() };
        do
        {
            var page = await client.ListSkillsAsync(requestParams, cancellationToken).ConfigureAwait(false);
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
    /// <returns>The page with every entry structurally validated, but not compared with the skill's files.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="client"/> or <paramref name="requestParams"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">The server did not declare the MCP Skills extension.</exception>
    /// <exception cref="InvalidDataException">The server returned an invalid skill entry.</exception>
    /// <exception cref="McpException">The request failed or the server returned an error response.</exception>
    /// <remarks>
    /// This method sends one request and preserves its page's cursor and cache hints. Use
    /// <see cref="ListSkillsAsync(McpClient, RequestOptions?, CancellationToken)"/> to retrieve all listing pages.
    /// An empty page does not establish that the server serves no skills; a skill can also be retrieved directly by URI.
    /// </remarks>
    [Experimental(Experimentals.Skills_DiagnosticId, UrlFormat = Experimentals.Skills_Url)]
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

        var result = await client.SendRequestAsync<ListSkillsRequestParams, ListSkillsResult>(
            SkillsProtocol.MethodSkillsList, requestParams, McpSkillsJsonContext.Default.Options,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        if (result.Skills is null || result.Skills.Any(static skill => skill is null))
        {
            throw new JsonException("The required 'skills' result must be an array of non-null entries.");
        }

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
    /// <param name="options">Optional metadata and progress token for the request.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests. The default is <see cref="CancellationToken.None"/>.</param>
    /// <returns>The structurally validated skill entry, not a verification of the file's frontmatter.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="client"/> or <paramref name="uri"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">The server did not declare the MCP Skills extension.</exception>
    /// <exception cref="McpException">
    /// The request failed or the server returned an error response, including <see cref="McpErrorCode.InvalidParams"/>
    /// when the server serves no skill at <paramref name="uri"/>.
    /// </exception>
    /// <exception cref="InvalidDataException">The entry is invalid or has a different URI than the one requested.</exception>
    /// <remarks>
    /// A server answers for every skill it serves, whether or not the skill appears in its listing. Use this to
    /// confirm a URI referenced from server instructions or another skill, and to refresh one skill's manifest
    /// without re-enumerating the catalog.
    /// </remarks>
    [Experimental(Experimentals.Skills_DiagnosticId, UrlFormat = Experimentals.Skills_Url)]
    public static async ValueTask<Skill> GetSkillAsync(this McpClient client, string uri, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
#if NET
        ArgumentNullException.ThrowIfNull(uri);
#else
        if (uri is null) throw new ArgumentNullException(nameof(uri));
#endif

        var result = await GetSkillAsync(client, new GetSkillRequestParams { Uri = uri, Meta = options?.GetMetaForRequest() }, cancellationToken).ConfigureAwait(false);
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
    /// <exception cref="McpException">
    /// The request failed or the server returned an error response.
    /// </exception>
    /// <exception cref="InvalidDataException">The entry is invalid or has a different URI than the one requested.</exception>
    [Experimental(Experimentals.Skills_DiagnosticId, UrlFormat = Experimentals.Skills_Url)]
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
        string requestedUri = requestParams.Uri;

        var result = await client.SendRequestAsync<GetSkillRequestParams, GetSkillResult>(
            SkillsProtocol.MethodSkillsGet, requestParams, McpSkillsJsonContext.Default.Options,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        if (result.Skill is null)
        {
            throw new JsonException("The required 'skill' result must be an object, not null.");
        }

        SkillValidation.ValidateReceived(result.Skill, SkillsProtocol.MethodSkillsGet);
        if (!SkillUriComparer.Instance.Equals(result.Skill.Uri, requestedUri))
        {
            throw new InvalidDataException(
                $"The server answered '{SkillsProtocol.MethodSkillsGet}' for '{requestedUri}' with the entry for '{result.Skill.Uri}'.");
        }

        return result;
    }

    /// <summary>
    /// Reads one of a skill's files through <c>resources/read</c> and verifies its URI, digest and size against the manifest.
    /// </summary>
    /// <param name="client">The client.</param>
    /// <param name="skill">The skill entry being acted on.</param>
    /// <param name="uri">The URI of the file to read. It must be listed in <paramref name="skill"/>'s manifest.</param>
    /// <param name="options">Optional metadata and progress token, forwarded to the resource read.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests. The default is <see cref="CancellationToken.None"/>.</param>
    /// <returns>The manifest-verified contents. Frontmatter verification remains the host's responsibility.</returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">
    /// <paramref name="skill"/>'s manifest is <see cref="SkillResources.Dynamic"/>. Such a skill offers no content
    /// integrity; read its files with <see cref="McpClient.ReadResourceAsync(string, RequestOptions?, CancellationToken)"/>
    /// only if the host permits content without digest verification. Frontmatter verification is still required.
    /// </exception>
    /// <exception cref="InvalidDataException">
    /// <paramref name="uri"/> is not listed in the manifest, the server's response does not contain contents for
    /// <paramref name="uri"/>, or any returned content's size or digest does not match its manifest entry.
    /// </exception>
    /// <exception cref="McpException">The request failed or the server returned an error response.</exception>
    /// <remarks>
    /// <para>
    /// An unlisted file is treated as a verification failure before any request is sent, because the manifest is
    /// complete and an unlisted file is a change to the skill. To read it, refresh the entry with
    /// <see cref="GetSkillAsync(McpClient, string, RequestOptions, CancellationToken)"/> and proceed from the new manifest.
    /// </para>
    /// <para>
    /// For <c>SKILL.md</c>, the host must also parse the fetched frontmatter and compare it with
    /// <see cref="Skill.Frontmatter"/> before loading. A digest match does not perform or replace that check.
    /// </para>
    /// </remarks>
    [Experimental(Experimentals.Skills_DiagnosticId, UrlFormat = Experimentals.Skills_Url)]
    public static async ValueTask<ReadResourceResult> ReadSkillResourceAsync(
        this McpClient client,
        Skill skill,
        string uri,
        RequestOptions? options = null,
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
            throw new InvalidDataException(
                $"'{uri}' is not listed in the manifest of skill '{skill.Uri}'. An unlisted file is a change to the skill; " +
                "refresh the entry with skills/get before reading it.");
        }

        var result = await client.ReadResourceAsync(uri, options, cancellationToken).ConfigureAwait(false);
        SkillVerifier.Verify(skill, uri, result);
        return result;
    }

    private static void ThrowIfSkillsNotSupported(McpClient client, string operationName)
    {
        if (!client.ServerCapabilities.SupportsSkills())
        {
            throw new InvalidOperationException(
                $"'{operationName}' requires the server to declare the '{SkillsProtocol.ExtensionId}' extension in its capabilities, and it did not.");
        }
    }
}
