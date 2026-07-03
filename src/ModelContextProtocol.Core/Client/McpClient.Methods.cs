using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ModelContextProtocol.Client;

/// <summary>
/// Represents an instance of a Model Context Protocol (MCP) client session that connects to and communicates with an MCP server.
/// </summary>
public abstract partial class McpClient : McpSession
{
    /// <summary>Creates an <see cref="McpClient"/>, connecting it to the specified server.</summary>
    /// <param name="clientTransport">The transport instance used to communicate with the server.</param>
    /// <param name="clientOptions">
    /// A client configuration object that specifies client capabilities and protocol version.
    /// If <see langword="null"/>, details based on the current process are used.
    /// </param>
    /// <param name="loggerFactory">A logger factory for creating loggers for clients.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests. The default is <see cref="CancellationToken.None"/>.</param>
    /// <returns>An <see cref="McpClient"/> that's connected to the specified server.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="clientTransport"/> is <see langword="null"/>.</exception>
    /// <exception cref="HttpRequestException">An error occurred while connecting to the server over HTTP.</exception>
    /// <exception cref="McpException">The server returned an error response during initialization.</exception>
    /// <remarks>
    /// <para>
    /// When using an HTTP-based transport (such as <see cref="HttpClientTransport"/>), this method may throw
    /// <see cref="HttpRequestException"/> if there is a problem establishing the connection to the MCP server.
    /// </para>
    /// <para>
    /// If the server requires authentication and credentials are not provided or are invalid, an
    /// <see cref="HttpRequestException"/> with an HTTP 401 Unauthorized status code will be thrown.
    /// To authenticate with a protected server, configure the <see cref="HttpClientTransportOptions.OAuth"/>
    /// property of the transport with appropriate credentials before calling this method.
    /// </para>
    /// </remarks>
    public static async Task<McpClient> CreateAsync(
        IClientTransport clientTransport,
        McpClientOptions? clientOptions = null,
        ILoggerFactory? loggerFactory = null,
        CancellationToken cancellationToken = default)
    {
        Throw.IfNull(clientTransport);

        var transport = await clientTransport.ConnectAsync(cancellationToken).ConfigureAwait(false);
        var endpointName = clientTransport.Name;

        var clientSession = new McpClientImpl(transport, endpointName, clientOptions, loggerFactory);
        try
        {
            await clientSession.ConnectAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not ClientTransportClosedException)
        {
            // ConnectAsync already disposed the session (which includes awaiting Completion).
            // Check if the transport provided structured completion details indicating
            // why the transport closed that aren't already in the original exception chain.
            Debug.Assert(clientSession.Completion.IsCompleted, "Completion should already be finished after ConnectAsync's DisposeAsync.");
            var completionDetails = await clientSession.Completion.ConfigureAwait(false);

            // If the transport closed with a non-graceful error (e.g., server process exited)
            // and the completion details carry an exception that's NOT already in the original
            // exception chain, throw a ClientTransportClosedException with the structured details so
            // callers can programmatically inspect the closure reason (exit code, stderr, etc.).
            // When the same exception is already in the chain (e.g., HttpRequestException from
            // an HTTP transport), the original exception is more appropriate to re-throw.
            if (completionDetails.Exception is { } detailsException &&
                !ExceptionChainContains(ex, detailsException))
            {
                throw new ClientTransportClosedException(completionDetails);
            }

            throw;
        }

        return clientSession;
    }

    /// <summary>
    /// Returns <see langword="true"/> if <paramref name="target"/> is the same object as
    /// <paramref name="exception"/> or any exception in its <see cref="Exception.InnerException"/> chain.
    /// </summary>
    private static bool ExceptionChainContains(Exception exception, Exception target)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (ReferenceEquals(current, target))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Recreates an <see cref="McpClient"/> using an existing transport session without sending a new initialize request.
    /// </summary>
    /// <param name="clientTransport">The transport instance already configured to connect to the target server.</param>
    /// <param name="resumeOptions">The metadata captured from the original session that should be applied when resuming.</param>
    /// <param name="clientOptions">Optional client settings that should mirror those used to create the original session.</param>
    /// <param name="loggerFactory">An optional logger factory for diagnostics.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests. The default is <see cref="CancellationToken.None"/>.</param>
    /// <returns>An <see cref="McpClient"/> bound to the resumed session.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="clientTransport"/>, <paramref name="resumeOptions"/>, <see cref="ResumeClientSessionOptions.ServerCapabilities"/>, or <see cref="ResumeClientSessionOptions.ServerInfo"/> is <see langword="null"/>.</exception>
    public static async Task<McpClient> ResumeSessionAsync(
        IClientTransport clientTransport,
        ResumeClientSessionOptions resumeOptions,
        McpClientOptions? clientOptions = null,
        ILoggerFactory? loggerFactory = null,
        CancellationToken cancellationToken = default)
    {
        Throw.IfNull(clientTransport);
        Throw.IfNull(resumeOptions);
        Throw.IfNull(resumeOptions.ServerCapabilities);
        Throw.IfNull(resumeOptions.ServerInfo);

        var transport = await clientTransport.ConnectAsync(cancellationToken).ConfigureAwait(false);

        McpClientImpl clientSession = new(transport, clientTransport.Name, clientOptions, loggerFactory);
        clientSession.ResumeSession(resumeOptions);
        
        return clientSession;
    }

    /// <summary>
    /// Sends a ping request to verify server connectivity.
    /// </summary>
    /// <param name="options">Optional request options including metadata, serialization settings, and progress tracking.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests. The default is <see cref="CancellationToken.None"/>.</param>
    /// <returns>A task containing the ping result.</returns>
    /// <exception cref="McpException">The server cannot be reached or returned an error response.</exception>
    public ValueTask<PingResult> PingAsync(RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        return PingAsync(
            new PingRequestParams
            {
                Meta = options?.GetMetaForRequest()
            },
            cancellationToken);
    }

    /// <summary>
    /// Sends a ping request to verify server connectivity.
    /// </summary>
    /// <param name="requestParams">The request parameters to send in the request.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests. The default is <see cref="CancellationToken.None"/>.</param>
    /// <returns>A task containing the ping result.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="requestParams"/> is <see langword="null"/>.</exception>
    /// <exception cref="McpException">The server cannot be reached or returned an error response.</exception>
    public ValueTask<PingResult> PingAsync(
        PingRequestParams requestParams,
        CancellationToken cancellationToken = default)
    {
        Throw.IfNull(requestParams);

        return SendRequestAsync(
            RequestMethods.Ping,
            requestParams,
            McpJsonUtilities.JsonContext.Default.PingRequestParams,
            McpJsonUtilities.JsonContext.Default.PingResult,
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Retrieves a list of available tools from the server.
    /// </summary>
    /// <param name="options">Optional request options including metadata, serialization settings, and progress tracking.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests. The default is <see cref="CancellationToken.None"/>.</param>
    /// <returns>A list of all available tools as <see cref="McpClientTool"/> instances.</returns>
    /// <exception cref="McpException">The request failed or the server returned an error response.</exception>
    /// <remarks>
    /// <para>
    /// This overload aggregates every page into a single list and does not surface the per-result caching hints
    /// (<see cref="ListToolsResult.TimeToLive"/> and <see cref="ListToolsResult.CacheScope"/>). To read those hints,
    /// use the <see cref="ListToolsAsync(ListToolsRequestParams, CancellationToken)"/> overload, which returns the
    /// raw <see cref="ListToolsResult"/> for each page.
    /// </para>
    /// <para>
    /// The SDK does not perform any internal caching of listing results; every call re-fetches all pages from the server.
    /// If you want to cache listing results, do so in your own code using the lower-level
    /// <see cref="ListToolsAsync(ListToolsRequestParams, CancellationToken)"/> overload, which exposes the per-page
    /// caching hints and lets you manage pagination so each page can be cached and expired independently.
    /// </para>
    /// </remarks>
    public async ValueTask<IList<McpClientTool>> ListToolsAsync(
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ToolCacheClearing?.Invoke();

        List<McpClientTool>? tools = null;
        ListToolsRequestParams requestParams = new() { Meta = options?.GetMetaForRequest() };
        do
        {
            var toolResults = await ListToolsAsync(requestParams, cancellationToken).ConfigureAwait(false);
            tools ??= new(toolResults.Tools.Count);
            foreach (var tool in toolResults.Tools)
            {
                // Validate x-mcp-header annotations per SEP-2243. The spec requires Streamable HTTP
                // clients to exclude tools with invalid annotations and permits other transports
                // (e.g., stdio) to ignore the annotations entirely. This client validates on all
                // transports so a malformed definition is rejected consistently regardless of transport.
                if (!McpHeaderExtractor.ValidateToolSchema(tool, out var rejectionReason))
                {
                    ToolRejected?.Invoke(tool, rejectionReason!);
                    continue;
                }

                ToolDiscovered?.Invoke(tool);
                tools.Add(new(this, tool, options?.JsonSerializerOptions));
            }

            requestParams.Cursor = toolResults.NextCursor;
        }
        while (requestParams.Cursor is not null);

        return tools;
    }

    /// <summary>
    /// Invoked when a tool definition is discovered from a <c>tools/list</c> response.
    /// </summary>
    internal Action<Tool>? ToolDiscovered;

    /// <summary>
    /// Invoked when a tool definition is rejected due to invalid <c>x-mcp-header</c> annotations.
    /// </summary>
    internal Action<Tool, string>? ToolRejected;

    /// <summary>
    /// Invoked before enumerating tools to clear any previously cached tool definitions.
    /// </summary>
    internal Action? ToolCacheClearing;

    /// <summary>
    /// Retrieves a list of available tools from the server.
    /// </summary>
    /// <param name="requestParams">The request parameters to send in the request.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests. The default is <see cref="CancellationToken.None"/>.</param>
    /// <returns>The result of the request as provided by the server.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="requestParams"/> is <see langword="null"/>.</exception>
    /// <exception cref="McpException">The request failed or the server returned an error response.</exception>
    /// <remarks>
    /// The <see cref="ListToolsAsync(RequestOptions?, CancellationToken)"/> overload retrieves all tools by automatically handling pagination.
    /// This overload works with the lower-level <see cref="ListToolsRequestParams"/> and <see cref="ListToolsResult"/>, returning the raw result from the server.
    /// Any pagination needs to be managed by the caller.
    /// </remarks>
    public ValueTask<ListToolsResult> ListToolsAsync(
        ListToolsRequestParams requestParams,
        CancellationToken cancellationToken = default)
    {
        Throw.IfNull(requestParams);

        return ValidateCacheableResultAsync(RequestMethods.ToolsList, SendRequestAsync(
            RequestMethods.ToolsList,
            requestParams,
            McpJsonUtilities.JsonContext.Default.ListToolsRequestParams,
            McpJsonUtilities.JsonContext.Default.ListToolsResult,
            cancellationToken: cancellationToken));
    }

    /// <summary>
    /// Awaits a cacheable result and gives derived clients a chance to emit diagnostics (for example, a
    /// SEP-2549 conformance warning) before returning it. Preserves the synchronous argument validation
    /// performed by the callers before the request is issued.
    /// </summary>
    private async ValueTask<TResult> ValidateCacheableResultAsync<TResult>(string method, ValueTask<TResult> resultTask)
        where TResult : ICacheableResult
    {
        var result = await resultTask.ConfigureAwait(false);
        ValidateCacheableResult(method, result);
        return result;
    }

    /// <summary>
    /// Retrieves a list of available prompts from the server.
    /// </summary>
    /// <param name="options">Optional request options including metadata, serialization settings, and progress tracking.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests. The default is <see cref="CancellationToken.None"/>.</param>
    /// <returns>A list of all available prompts as <see cref="McpClientPrompt"/> instances.</returns>
    /// <exception cref="McpException">The request failed or the server returned an error response.</exception>
    /// <remarks>
    /// <para>
    /// This overload aggregates every page into a single list and does not surface the per-result caching hints
    /// (<see cref="ListPromptsResult.TimeToLive"/> and <see cref="ListPromptsResult.CacheScope"/>). To read those hints,
    /// use the <see cref="ListPromptsAsync(ListPromptsRequestParams, CancellationToken)"/> overload, which returns the
    /// raw <see cref="ListPromptsResult"/> for each page.
    /// </para>
    /// <para>
    /// The SDK does not perform any internal caching of listing results; every call re-fetches all pages from the server.
    /// If you want to cache listing results, do so in your own code using the lower-level
    /// <see cref="ListPromptsAsync(ListPromptsRequestParams, CancellationToken)"/> overload, which exposes the per-page
    /// caching hints and lets you manage pagination so each page can be cached and expired independently.
    /// </para>
    /// </remarks>
    public async ValueTask<IList<McpClientPrompt>> ListPromptsAsync(
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        List<McpClientPrompt>? prompts = null;
        ListPromptsRequestParams requestParams = new() { Meta = options?.GetMetaForRequest() };
        do
        {
            var promptResults = await ListPromptsAsync(requestParams, cancellationToken).ConfigureAwait(false);
            prompts ??= new(promptResults.Prompts.Count);
            foreach (var prompt in promptResults.Prompts)
            {
                prompts.Add(new(this, prompt));
            }

            requestParams.Cursor = promptResults.NextCursor;
        }
        while (requestParams.Cursor is not null);

        return prompts;
    }

    /// <summary>
    /// Retrieves a list of available prompts from the server.
    /// </summary>
    /// <param name="requestParams">The request parameters to send in the request.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests. The default is <see cref="CancellationToken.None"/>.</param>
    /// <returns>The result of the request as provided by the server.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="requestParams"/> is <see langword="null"/>.</exception>
    /// <exception cref="McpException">The request failed or the server returned an error response.</exception>
    /// <remarks>
    /// The <see cref="ListPromptsAsync(RequestOptions?, CancellationToken)"/> overload retrieves all prompts by automatically handling pagination.
    /// This overload works with the lower-level <see cref="ListPromptsRequestParams"/> and <see cref="ListPromptsResult"/>, returning the raw result from the server.
    /// Any pagination needs to be managed by the caller.
    /// </remarks>
    public ValueTask<ListPromptsResult> ListPromptsAsync(
        ListPromptsRequestParams requestParams,
        CancellationToken cancellationToken = default)
    {
        Throw.IfNull(requestParams);

        return ValidateCacheableResultAsync(RequestMethods.PromptsList, SendRequestAsync(
            RequestMethods.PromptsList,
            requestParams,
            McpJsonUtilities.JsonContext.Default.ListPromptsRequestParams,
            McpJsonUtilities.JsonContext.Default.ListPromptsResult,
            cancellationToken: cancellationToken));
    }

    /// <summary>
    /// Retrieves a specific prompt from the MCP server.
    /// </summary>
    /// <param name="name">The name of the prompt to retrieve.</param>
    /// <param name="arguments">Optional arguments for the prompt. The dictionary keys are parameter names, and the values are the argument values.</param>
    /// <param name="options">Optional request options including metadata, serialization settings, and progress tracking.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests. The default is <see cref="CancellationToken.None"/>.</param>
    /// <returns>A task containing the prompt's result with content and messages.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="name"/> is empty or composed entirely of whitespace.</exception>
    /// <exception cref="McpException">The request failed or the server returned an error response.</exception>
    public ValueTask<GetPromptResult> GetPromptAsync(
        string name,
        IReadOnlyDictionary<string, object?>? arguments = null,
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        Throw.IfNullOrWhiteSpace(name);

        var serializerOptions = options?.JsonSerializerOptions ?? McpJsonUtilities.DefaultOptions;
        serializerOptions.MakeReadOnly();

        return GetPromptAsync(
            new GetPromptRequestParams 
            {
                Name = name, 
                Arguments = ToArgumentsDictionary(arguments, serializerOptions),
                Meta = options?.GetMetaForRequest(),
            },
            cancellationToken);
    }

    /// <summary>
    /// Retrieves a specific prompt from the MCP server.
    /// </summary>
    /// <param name="requestParams">The request parameters to send in the request.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests. The default is <see cref="CancellationToken.None"/>.</param>
    /// <returns>The result of the request as provided by the server.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="requestParams"/> is <see langword="null"/>.</exception>
    /// <exception cref="McpException">The request failed or the server returned an error response.</exception>
    public ValueTask<GetPromptResult> GetPromptAsync(
        GetPromptRequestParams requestParams,
        CancellationToken cancellationToken = default)
    {
        Throw.IfNull(requestParams);

        return SendRequestAsync(
            RequestMethods.PromptsGet,
            requestParams,
            McpJsonUtilities.JsonContext.Default.GetPromptRequestParams,
            McpJsonUtilities.JsonContext.Default.GetPromptResult,
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Retrieves a list of available resource templates from the server.
    /// </summary>
    /// <param name="options">Optional request options including metadata, serialization settings, and progress tracking.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests. The default is <see cref="CancellationToken.None"/>.</param>
    /// <returns>A list of all available resource templates as <see cref="ResourceTemplate"/> instances.</returns>
    /// <exception cref="McpException">The request failed or the server returned an error response.</exception>
    /// <remarks>
    /// <para>
    /// This overload aggregates every page into a single list and does not surface the per-result caching hints
    /// (<see cref="ListResourceTemplatesResult.TimeToLive"/> and <see cref="ListResourceTemplatesResult.CacheScope"/>). To read those hints,
    /// use the <see cref="ListResourceTemplatesAsync(ListResourceTemplatesRequestParams, CancellationToken)"/> overload, which returns the
    /// raw <see cref="ListResourceTemplatesResult"/> for each page.
    /// </para>
    /// <para>
    /// The SDK does not perform any internal caching of listing results; every call re-fetches all pages from the server.
    /// If you want to cache listing results, do so in your own code using the lower-level
    /// <see cref="ListResourceTemplatesAsync(ListResourceTemplatesRequestParams, CancellationToken)"/> overload, which exposes the per-page
    /// caching hints and lets you manage pagination so each page can be cached and expired independently.
    /// </para>
    /// </remarks>
    public async ValueTask<IList<McpClientResourceTemplate>> ListResourceTemplatesAsync(
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        List<McpClientResourceTemplate>? resourceTemplates = null;
        ListResourceTemplatesRequestParams requestParams = new() { Meta = options?.GetMetaForRequest() };
        do
        {
            var templateResults = await ListResourceTemplatesAsync(requestParams, cancellationToken).ConfigureAwait(false);
            resourceTemplates ??= new(templateResults.ResourceTemplates.Count);
            foreach (var template in templateResults.ResourceTemplates)
            {
                resourceTemplates.Add(new(this, template));
            }

            requestParams.Cursor = templateResults.NextCursor;
        }
        while (requestParams.Cursor is not null);

        return resourceTemplates;
    }

    /// <summary>
    /// Retrieves a list of available resource templates from the server.
    /// </summary>
    /// <param name="requestParams">The request parameters to send in the request.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests. The default is <see cref="CancellationToken.None"/>.</param>
    /// <returns>The result of the request as provided by the server.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="requestParams"/> is <see langword="null"/>.</exception>
    /// <exception cref="McpException">The request failed or the server returned an error response.</exception>
    /// <remarks>
    /// The <see cref="ListResourceTemplatesAsync(RequestOptions?, CancellationToken)"/> overload retrieves all resource templates by automatically handling pagination.
    /// This overload works with the lower-level <see cref="ListResourceTemplatesRequestParams"/> and <see cref="ListResourceTemplatesResult"/>, returning the raw result from the server.
    /// Any pagination needs to be managed by the caller.
    /// </remarks>
    public ValueTask<ListResourceTemplatesResult> ListResourceTemplatesAsync(
        ListResourceTemplatesRequestParams requestParams,
        CancellationToken cancellationToken = default)
    {
        Throw.IfNull(requestParams);

        return ValidateCacheableResultAsync(RequestMethods.ResourcesTemplatesList, SendRequestAsync(
            RequestMethods.ResourcesTemplatesList,
            requestParams,
            McpJsonUtilities.JsonContext.Default.ListResourceTemplatesRequestParams,
            McpJsonUtilities.JsonContext.Default.ListResourceTemplatesResult,
            cancellationToken: cancellationToken));
    }

    /// <summary>
    /// Retrieves a list of available resources from the server.
    /// </summary>
    /// <param name="options">Optional request options including metadata, serialization settings, and progress tracking.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests. The default is <see cref="CancellationToken.None"/>.</param>
    /// <returns>A list of all available resources as <see cref="Resource"/> instances.</returns>
    /// <exception cref="McpException">The request failed or the server returned an error response.</exception>
    /// <remarks>
    /// <para>
    /// This overload aggregates every page into a single list and does not surface the per-result caching hints
    /// (<see cref="ListResourcesResult.TimeToLive"/> and <see cref="ListResourcesResult.CacheScope"/>). To read those hints,
    /// use the <see cref="ListResourcesAsync(ListResourcesRequestParams, CancellationToken)"/> overload, which returns the
    /// raw <see cref="ListResourcesResult"/> for each page.
    /// </para>
    /// <para>
    /// The SDK does not perform any internal caching of listing results; every call re-fetches all pages from the server.
    /// If you want to cache listing results, do so in your own code using the lower-level
    /// <see cref="ListResourcesAsync(ListResourcesRequestParams, CancellationToken)"/> overload, which exposes the per-page
    /// caching hints and lets you manage pagination so each page can be cached and expired independently.
    /// </para>
    /// </remarks>
    public async ValueTask<IList<McpClientResource>> ListResourcesAsync(
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        List<McpClientResource>? resources = null;
        ListResourcesRequestParams requestParams = new() { Meta = options?.GetMetaForRequest() };
        do
        {
            var resourceResults = await ListResourcesAsync(requestParams, cancellationToken).ConfigureAwait(false);
            resources ??= new(resourceResults.Resources.Count);
            foreach (var resource in resourceResults.Resources)
            {
                resources.Add(new(this, resource));
            }

            requestParams.Cursor = resourceResults.NextCursor;
        }
        while (requestParams.Cursor is not null);

        return resources;
    }

    /// <summary>
    /// Retrieves a list of available resources from the server.
    /// </summary>
    /// <param name="requestParams">The request parameters to send in the request.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests. The default is <see cref="CancellationToken.None"/>.</param>
    /// <returns>The result of the request as provided by the server.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="requestParams"/> is <see langword="null"/>.</exception>
    /// <exception cref="McpException">The request failed or the server returned an error response.</exception>
    /// <remarks>
    /// The <see cref="ListResourcesAsync(RequestOptions?, CancellationToken)"/> overload retrieves all resources by automatically handling pagination.
    /// This overload works with the lower-level <see cref="ListResourcesRequestParams"/> and <see cref="ListResourcesResult"/>, returning the raw result from the server.
    /// Any pagination needs to be managed by the caller.
    /// </remarks>
    public ValueTask<ListResourcesResult> ListResourcesAsync(
        ListResourcesRequestParams requestParams,
        CancellationToken cancellationToken = default)
    {
        Throw.IfNull(requestParams);

        return ValidateCacheableResultAsync(RequestMethods.ResourcesList, SendRequestAsync(
            RequestMethods.ResourcesList,
            requestParams,
            McpJsonUtilities.JsonContext.Default.ListResourcesRequestParams,
            McpJsonUtilities.JsonContext.Default.ListResourcesResult,
            cancellationToken: cancellationToken));
    }
        
    /// <summary>
    /// Reads a resource from the server.
    /// </summary>
    /// <param name="uri">The URI of the resource.</param>
    /// <param name="options">Optional request options including metadata, serialization settings, and progress tracking.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests. The default is <see cref="CancellationToken.None"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="uri"/> is <see langword="null"/>.</exception>
    /// <exception cref="McpException">The request failed or the server returned an error response.</exception>
    public ValueTask<ReadResourceResult> ReadResourceAsync(
        Uri uri, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        Throw.IfNull(uri);

        return ReadResourceAsync(uri.AbsoluteUri, options, cancellationToken);
    }

    /// <summary>
    /// Reads a resource from the server.
    /// </summary>
    /// <param name="uri">The URI of the resource.</param>
    /// <param name="options">Optional request options including metadata, serialization settings, and progress tracking.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests. The default is <see cref="CancellationToken.None"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="uri"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="uri"/> is empty or composed entirely of whitespace.</exception>
    /// <exception cref="McpException">The request failed or the server returned an error response.</exception>
    public ValueTask<ReadResourceResult> ReadResourceAsync(
        string uri, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        Throw.IfNullOrWhiteSpace(uri);

        return ReadResourceAsync(new ReadResourceRequestParams
        {
            Uri = uri,
            Meta = options?.GetMetaForRequest(),
        }, cancellationToken);
    }

    /// <summary>
    /// Reads a resource from the server.
    /// </summary>
    /// <param name="uriTemplate">The URI template of the resource.</param>
    /// <param name="arguments">Arguments to use to format <paramref name="uriTemplate"/>.</param>
    /// <param name="options">Optional request options including metadata, serialization settings, and progress tracking.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests. The default is <see cref="CancellationToken.None"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="uriTemplate"/> or <paramref name="arguments"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="uriTemplate"/> is empty or composed entirely of whitespace.</exception>
    /// <exception cref="McpException">The request failed or the server returned an error response.</exception>
    public ValueTask<ReadResourceResult> ReadResourceAsync(
        string uriTemplate, IReadOnlyDictionary<string, object?> arguments, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        Throw.IfNullOrWhiteSpace(uriTemplate);
        Throw.IfNull(arguments);

        return ReadResourceAsync(
            new ReadResourceRequestParams 
            {
                Uri = UriTemplate.FormatUri(uriTemplate, arguments),
                Meta = options?.GetMetaForRequest(),
            },
            cancellationToken);
    }

    /// <summary>
    /// Reads a resource from the server.
    /// </summary>
    /// <param name="requestParams">The request parameters to send in the request.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests. The default is <see cref="CancellationToken.None"/>.</param>
    /// <returns>The result of the request.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="requestParams"/> is <see langword="null"/>.</exception>
    /// <exception cref="McpException">The request failed or the server returned an error response.</exception>
    public ValueTask<ReadResourceResult> ReadResourceAsync(
        ReadResourceRequestParams requestParams,
        CancellationToken cancellationToken = default)
    {
        Throw.IfNull(requestParams);

        return ValidateCacheableResultAsync(RequestMethods.ResourcesRead, SendRequestAsync(
            RequestMethods.ResourcesRead,
            requestParams,
            McpJsonUtilities.JsonContext.Default.ReadResourceRequestParams,
            McpJsonUtilities.JsonContext.Default.ReadResourceResult,
            cancellationToken: cancellationToken));
    }

    /// <summary>
    /// Requests completion suggestions for a prompt argument or resource reference.
    /// </summary>
    /// <param name="reference">The reference object specifying the type and optional URI or name.</param>
    /// <param name="argumentName">The name of the argument for which completions are requested.</param>
    /// <param name="argumentValue">The current value of the argument, used to filter relevant completions.</param>
    /// <param name="options">Optional request options including metadata, serialization settings, and progress tracking.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests. The default is <see cref="CancellationToken.None"/>.</param>
    /// <returns>A <see cref="CompleteResult"/> containing completion suggestions.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="reference"/> or <paramref name="argumentName"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="argumentName"/> is empty or composed entirely of whitespace.</exception>
    /// <exception cref="McpException">The request failed or the server returned an error response.</exception>
    public ValueTask<CompleteResult> CompleteAsync(
        Reference reference, string argumentName, string argumentValue,
        RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        Throw.IfNull(reference);
        Throw.IfNullOrWhiteSpace(argumentName);

        return CompleteAsync(
            new CompleteRequestParams
            {
                Ref = reference,
                Argument = new() { Name = argumentName, Value = argumentValue },
                Meta = options?.GetMetaForRequest(),
            },
            cancellationToken);
    }

    /// <summary>
    /// Requests completion suggestions for a prompt argument or resource reference.
    /// </summary>
    /// <param name="requestParams">The request parameters to send in the request.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests. The default is <see cref="CancellationToken.None"/>.</param>
    /// <returns>The result of the request.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="requestParams"/> is <see langword="null"/>.</exception>
    /// <exception cref="McpException">The request failed or the server returned an error response.</exception>
    public ValueTask<CompleteResult> CompleteAsync(
        CompleteRequestParams requestParams, 
        CancellationToken cancellationToken = default)
    {
        Throw.IfNull(requestParams);

        return SendRequestAsync(
            RequestMethods.CompletionComplete,
            requestParams,
            McpJsonUtilities.JsonContext.Default.CompleteRequestParams,
            McpJsonUtilities.JsonContext.Default.CompleteResult,
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Subscribes to a resource on the server to receive notifications when it changes.
    /// </summary>
    /// <param name="uri">The URI of the resource to subscribe to.</param>
    /// <param name="options">Optional request options including metadata, serialization settings, and progress tracking.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests. The default is <see cref="CancellationToken.None"/>.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="uri"/> is <see langword="null"/>.</exception>
    /// <exception cref="McpException">The request failed or the server returned an error response.</exception>
    public Task SubscribeToResourceAsync(Uri uri, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        Throw.IfNull(uri);

        return SubscribeToResourceAsync(uri.AbsoluteUri, options, cancellationToken);
    }

    /// <summary>
    /// Subscribes to a resource on the server to receive notifications when it changes.
    /// </summary>
    /// <param name="uri">The URI of the resource to which to subscribe.</param>
    /// <param name="options">Optional request options including metadata, serialization settings, and progress tracking.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests. The default is <see cref="CancellationToken.None"/>.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="uri"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="uri"/> is empty or composed entirely of whitespace.</exception>
    /// <exception cref="McpException">The request failed or the server returned an error response.</exception>
    public Task SubscribeToResourceAsync(string uri, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        Throw.IfNullOrWhiteSpace(uri);

        return SubscribeToResourceAsync(
            new SubscribeRequestParams
            {
                Uri = uri,
                Meta = options?.GetMetaForRequest(),
            }, 
            cancellationToken);
    }

    /// <summary>
    /// Subscribes to a resource on the server to receive notifications when it changes.
    /// </summary>
    /// <param name="requestParams">The request parameters to send in the request.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests. The default is <see cref="CancellationToken.None"/>.</param>
    /// <returns>The result of the request.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="requestParams"/> is <see langword="null"/>.</exception>
    /// <exception cref="McpException">The request failed or the server returned an error response.</exception>
    /// <remarks>
    /// <para>
    /// This method subscribes to resource update notifications but does not register a handler.
    /// To receive notifications, you must separately call <see cref="McpSession.RegisterNotificationHandler(string, Func{JsonRpcNotification, CancellationToken, ValueTask})"/>
    /// with <see cref="NotificationMethods.ResourceUpdatedNotification"/> and filter for the specific resource URI.
    /// To unsubscribe, call <see cref="UnsubscribeFromResourceAsync(UnsubscribeRequestParams, CancellationToken)"/> and dispose the handler registration.
    /// </para>
    /// <para>
    /// For a simpler API that handles both subscription and notification registration in a single call,
    /// use <see cref="SubscribeToResourceAsync(Uri, Func{ResourceUpdatedNotificationParams, CancellationToken, ValueTask}, RequestOptions?, CancellationToken)"/>.
    /// </para>
    /// </remarks>
    public Task SubscribeToResourceAsync(
        SubscribeRequestParams requestParams,
        CancellationToken cancellationToken = default)
    {
        Throw.IfNull(requestParams);

        return SendRequestAsync(
            RequestMethods.ResourcesSubscribe,
            requestParams,
            McpJsonUtilities.JsonContext.Default.SubscribeRequestParams,
            McpJsonUtilities.JsonContext.Default.EmptyResult,
            cancellationToken: cancellationToken).AsTask();
    }

    /// <summary>
    /// Subscribes to a resource on the server and registers a handler for notifications when it changes.
    /// </summary>
    /// <param name="uri">The URI of the resource to which to subscribe.</param>
    /// <param name="handler">The handler to invoke when the resource is updated. It receives <see cref="ResourceUpdatedNotificationParams"/> for the subscribed resource.</param>
    /// <param name="options">Optional request options including metadata, serialization settings, and progress tracking.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests. The default is <see cref="CancellationToken.None"/>.</param>
    /// <returns>
    /// A task that completes with an <see cref="IAsyncDisposable"/> that, when disposed, unsubscribes from the resource
    /// and removes the notification handler.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="uri"/> or <paramref name="handler"/> is <see langword="null"/>.</exception>
    /// <exception cref="McpException">The request failed or the server returned an error response.</exception>
    /// <remarks>
    /// <para>
    /// This method provides a convenient way to subscribe to resource updates and handle notifications in a single call.
    /// The returned <see cref="IAsyncDisposable"/> manages both the subscription and the notification handler registration.
    /// When disposed, it automatically unsubscribes from the resource and removes the handler.
    /// </para>
    /// <para>
    /// The handler will only be invoked for notifications related to the specified resource URI.
    /// Notifications for other resources are filtered out automatically.
    /// </para>
    /// </remarks>
    public Task<IAsyncDisposable> SubscribeToResourceAsync(
        Uri uri,
        Func<ResourceUpdatedNotificationParams, CancellationToken, ValueTask> handler,
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        Throw.IfNull(uri);

        return SubscribeToResourceAsync(uri.AbsoluteUri, handler, options, cancellationToken);
    }

    /// <summary>
    /// Subscribes to a resource on the server and registers a handler for notifications when it changes.
    /// </summary>
    /// <param name="uri">The URI of the resource to which to subscribe.</param>
    /// <param name="handler">The handler to invoke when the resource is updated. It receives <see cref="ResourceUpdatedNotificationParams"/> for the subscribed resource.</param>
    /// <param name="options">Optional request options including metadata, serialization settings, and progress tracking.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests. The default is <see cref="CancellationToken.None"/>.</param>
    /// <returns>
    /// A task that completes with an <see cref="IAsyncDisposable"/> that, when disposed, unsubscribes from the resource
    /// and removes the notification handler.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="uri"/> or <paramref name="handler"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="uri"/> is empty or composed entirely of whitespace.</exception>
    /// <exception cref="McpException">The request failed or the server returned an error response.</exception>
    /// <remarks>
    /// <para>
    /// This method provides a convenient way to subscribe to resource updates and handle notifications in a single call.
    /// The returned <see cref="IAsyncDisposable"/> manages both the subscription and the notification handler registration.
    /// When disposed, it automatically unsubscribes from the resource and removes the handler.
    /// </para>
    /// <para>
    /// The handler will only be invoked for notifications related to the specified resource URI.
    /// Notifications for other resources are filtered out automatically.
    /// </para>
    /// </remarks>
    public async Task<IAsyncDisposable> SubscribeToResourceAsync(
        string uri,
        Func<ResourceUpdatedNotificationParams, CancellationToken, ValueTask> handler,
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        Throw.IfNullOrWhiteSpace(uri);
        Throw.IfNull(handler);

        // Register a notification handler that filters for this specific resource
        IAsyncDisposable handlerRegistration = RegisterNotificationHandler(
            NotificationMethods.ResourceUpdatedNotification,
            async (notification, ct) =>
            {
                if (JsonSerializer.Deserialize(notification.Params, McpJsonUtilities.JsonContext.Default.ResourceUpdatedNotificationParams) is { } resourceUpdate &&
                    UriTemplate.UriTemplateComparer.Instance.Equals(resourceUpdate.Uri, uri))
                {
                    await handler(resourceUpdate, ct).ConfigureAwait(false);
                }
            });

        try
        {
            // Subscribe to the resource
            await SubscribeToResourceAsync(uri, options, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // If subscription fails, unregister the handler before propagating the exception
            await handlerRegistration.DisposeAsync().ConfigureAwait(false);
            throw;
        }

        // Return a disposable that unsubscribes and removes the handler
        return new ResourceSubscription(this, uri, handlerRegistration, options);
    }

    /// <summary>
    /// Manages a resource subscription, handling both unsubscription and handler disposal.
    /// </summary>
    private sealed class ResourceSubscription : IAsyncDisposable
    {
        private readonly McpClient _client;
        private readonly string _uri;
        private readonly IAsyncDisposable _handlerRegistration;
        private readonly RequestOptions? _options;
        private int _disposed;

        public ResourceSubscription(McpClient client, string uri, IAsyncDisposable handlerRegistration, RequestOptions? options)
        {
            _client = client;
            _uri = uri;
            _handlerRegistration = handlerRegistration;
            _options = options;
        }

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            try
            {
                // Unsubscribe from the resource
                await _client.UnsubscribeFromResourceAsync(_uri, _options, CancellationToken.None).ConfigureAwait(false);
            }
            finally
            {
                // Dispose the notification handler registration
                await _handlerRegistration.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Unsubscribes from a resource on the server to stop receiving notifications about its changes.
    /// </summary>
    /// <param name="uri">The URI of the resource to unsubscribe from.</param>
    /// <param name="options">Optional request options including metadata, serialization settings, and progress tracking.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests. The default is <see cref="CancellationToken.None"/>.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="uri"/> is <see langword="null"/>.</exception>
    /// <exception cref="McpException">The request failed or the server returned an error response.</exception>
    public Task UnsubscribeFromResourceAsync(Uri uri, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        Throw.IfNull(uri);

        return UnsubscribeFromResourceAsync(uri.AbsoluteUri, options, cancellationToken);
    }

    /// <summary>
    /// Unsubscribes from a resource on the server to stop receiving notifications about its changes.
    /// </summary>
    /// <param name="uri">The URI of the resource to unsubscribe from.</param>
    /// <param name="options">Optional request options including metadata, serialization settings, and progress tracking.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests. The default is <see cref="CancellationToken.None"/>.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="uri"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="uri"/> is empty or composed entirely of whitespace.</exception>
    /// <exception cref="McpException">The request failed or the server returned an error response.</exception>
    public Task UnsubscribeFromResourceAsync(string uri, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        Throw.IfNullOrWhiteSpace(uri);

        return UnsubscribeFromResourceAsync(
            new UnsubscribeRequestParams 
            {
                Uri = uri,
                Meta = options?.GetMetaForRequest()
            },
            cancellationToken);
    }

    /// <summary>
    /// Unsubscribes from a resource on the server to stop receiving notifications about its changes.
    /// </summary>
    /// <param name="requestParams">The request parameters to send in the request.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests. The default is <see cref="CancellationToken.None"/>.</param>
    /// <returns>The result of the request.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="requestParams"/> is <see langword="null"/>.</exception>
    /// <exception cref="McpException">The request failed or the server returned an error response.</exception>
    public Task UnsubscribeFromResourceAsync(
        UnsubscribeRequestParams requestParams,
        CancellationToken cancellationToken = default)
    {
        Throw.IfNull(requestParams);

        return SendRequestAsync(
            RequestMethods.ResourcesUnsubscribe,
            requestParams,
            McpJsonUtilities.JsonContext.Default.UnsubscribeRequestParams,
            McpJsonUtilities.JsonContext.Default.EmptyResult,
            cancellationToken: cancellationToken).AsTask();
    }

    /// <summary>
    /// Invokes a tool on the server.
    /// </summary>
    /// <param name="toolName">The name of the tool to call on the server.</param>
    /// <param name="arguments">An optional dictionary of arguments to pass to the tool.</param>
    /// <param name="progress">An optional progress reporter for server notifications.</param>
    /// <param name="options">Optional request options including metadata, serialization settings, and progress tracking.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests. The default is <see cref="CancellationToken.None"/>.</param>
    /// <returns>The <see cref="CallToolResult"/> from the tool execution.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="toolName"/> is <see langword="null"/>.</exception>
    /// <exception cref="McpException">The request failed or the server returned an error response.</exception>
    /// <remarks>
    /// This overload supports the tasks extension transparently. If the server responds with a
    /// task handle rather than an immediate result, this method polls <c>tasks/get</c> until the
    /// task completes, dispatching any <see cref="McpTaskStatus.InputRequired"/> entries through
    /// the client's registered sampling and elicitation handlers along the way. Use
    /// <see cref="CallToolRawAsync"/> to disable automatic polling.
    /// </remarks>
    public ValueTask<CallToolResult> CallToolAsync(
        string toolName,
        IReadOnlyDictionary<string, object?>? arguments = null,
        IProgress<ProgressNotificationValue>? progress = null,
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        Throw.IfNull(toolName);

        var serializerOptions = options?.JsonSerializerOptions ?? McpJsonUtilities.DefaultOptions;
        serializerOptions.MakeReadOnly();

        if (progress is null)
        {
            return CallToolAsync(
                new CallToolRequestParams
                {
                    Name = toolName,
                    Arguments = ToArgumentsDictionary(arguments, serializerOptions),
                    Meta = options?.GetMetaForRequest(),
                },
                cancellationToken);
        }

        return SendRequestWithProgressAsync(toolName, arguments, progress, options?.GetMetaForRequest(), serializerOptions, cancellationToken);

        async ValueTask<CallToolResult> SendRequestWithProgressAsync(
            string toolName,
            IReadOnlyDictionary<string, object?>? arguments,
            IProgress<ProgressNotificationValue> progress,
            JsonObject? meta,
            JsonSerializerOptions serializerOptions,
            CancellationToken cancellationToken)
        {
            ProgressToken progressToken = new(Guid.NewGuid().ToString("N"));

            await using var _ = RegisterNotificationHandler(NotificationMethods.ProgressNotification,
                (notification, cancellationToken) =>
                {
                    if (JsonSerializer.Deserialize(notification.Params, McpJsonUtilities.JsonContext.Default.ProgressNotificationParams) is { } pn &&
                        pn.ProgressToken == progressToken)
                    {
                        progress.Report(pn.Progress);
                    }

                    return default;
                }).ConfigureAwait(false);

            JsonObject metaWithProgress = meta is not null ? (JsonObject)meta.DeepClone() : [];
            metaWithProgress["progressToken"] = progressToken.ToString();

            return await CallToolAsync(
                new()
                {
                    Name = toolName,
                    Arguments = ToArgumentsDictionary(arguments, serializerOptions),
                    Meta = metaWithProgress,
                },
                cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Invokes a tool on the server.
    /// </summary>
    /// <param name="requestParams">The request parameters to send in the request.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests. The default is <see cref="CancellationToken.None"/>.</param>
    /// <returns>The result of the request.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="requestParams"/> is <see langword="null"/>.</exception>
    /// <exception cref="McpException">The request failed or the server returned an error response.</exception>
    /// <remarks>
    /// This method automatically includes the <c>io.modelcontextprotocol/tasks</c> extension capability
    /// in the request metadata. If the server returns a task handle instead of an immediate result,
    /// this method transparently polls <c>tasks/get</c> until the task completes, fails, or is cancelled.
    /// Use <see cref="CallToolRawAsync"/>
    /// to receive the raw <see cref="ResultOrCreatedTask{TResult}"/> without automatic polling.
    /// </remarks>
    public async ValueTask<CallToolResult> CallToolAsync(
        CallToolRequestParams requestParams,
        CancellationToken cancellationToken = default)
    {
        Throw.IfNull(requestParams);

        var augmented = await CallToolRawAsync(requestParams, cancellationToken).ConfigureAwait(false);

        if (!augmented.IsTask)
        {
            return augmented.Result!;
        }

        return await PollTaskToCompletionAsync(augmented.TaskCreated!, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Polls a task until it reaches a terminal state and returns the final <see cref="CallToolResult"/>.
    /// </summary>
    private async ValueTask<CallToolResult> PollTaskToCompletionAsync(
        CreateTaskResult taskCreated,
        CancellationToken cancellationToken)
    {
        // If the server claims InputRequired but never publishes new input requests after we have
        // already responded to everything it asked for, treat that as a stuck task. The client
        // can still cancel earlier via cancellationToken; this guard prevents an unbounded poll
        // loop when the server is misbehaving. The threshold is configurable via
        // McpClientOptions.MaxConsecutiveStuckPolls.
        int maxConsecutiveStuckPolls = MaxConsecutiveStuckPolls;

        string taskId = taskCreated.TaskId;
        long pollIntervalMs = taskCreated.PollIntervalMs ?? 1000;
        HashSet<string>? resolvedRequestKeys = null;
        bool isFirstPoll = true;
        int consecutiveStuckPolls = 0;

        while (true)
        {
            // Skip the delay before the first poll: many tasks complete almost immediately and we
            // don't want to pay the poll interval as gratuitous latency.
            if (!isFirstPoll)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(pollIntervalMs), cancellationToken).ConfigureAwait(false);
            }
            isFirstPoll = false;

            var taskResult = await GetTaskAsync(taskId, cancellationToken).ConfigureAwait(false);

            // Update poll interval if the server changed it.
            if (taskResult.PollIntervalMs is { } newInterval)
            {
                pollIntervalMs = newInterval;
            }

            switch (taskResult)
            {
                case CompletedTaskResult completed:
                    return JsonSerializer.Deserialize(completed.Result, McpJsonUtilities.JsonContext.Default.CallToolResult)
                        ?? throw new JsonException("Failed to deserialize CallToolResult from completed task.");

                case FailedTaskResult failed:
                    throw new McpException($"Task '{taskId}' failed: {failed.Error}");

                case CancelledTaskResult:
                    throw new OperationCanceledException($"Task '{taskId}' was cancelled by the server.");

                case InputRequiredTaskResult inputRequired:
                    // Dedup: only resolve input requests we haven't already responded to.
                    var newRequests = new Dictionary<string, InputRequest>();
                    if (inputRequired.InputRequests is { } incomingRequests)
                    {
                        foreach (var kvp in incomingRequests)
                        {
                            if (resolvedRequestKeys is null || !resolvedRequestKeys.Contains(kvp.Key))
                            {
                                newRequests[kvp.Key] = kvp.Value;
                            }
                        }
                    }

                    if (newRequests.Count > 0)
                    {
                        consecutiveStuckPolls = 0;

                        IDictionary<string, InputResponse> inputResponses;
                        try
                        {
                            inputResponses = await ResolveInputRequestsAsync(newRequests, cancellationToken).ConfigureAwait(false);
                        }
                        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                        {
                            throw;
                        }
                        catch
                        {
                            // The input handler failed (e.g., ElicitationHandler threw or no handler was registered).
                            // Best-effort cancel of the server-side task so it doesn't stay stuck in InputRequired
                            // until TTL expires.
                            try
                            {
                                await CancelTaskAsync(taskId, CancellationToken.None).ConfigureAwait(false);
                            }
                            catch
                            {
                                // Swallow secondary failures; we're already propagating the original exception.
                            }

                            throw;
                        }

                        await UpdateTaskAsync(new UpdateTaskRequestParams
                        {
                            TaskId = taskId,
                            InputResponses = inputResponses,
                        }, cancellationToken).ConfigureAwait(false);

                        resolvedRequestKeys ??= new HashSet<string>(StringComparer.Ordinal);
                        foreach (var key in inputResponses.Keys)
                        {
                            resolvedRequestKeys.Add(key);
                        }
                    }
                    else if (++consecutiveStuckPolls >= maxConsecutiveStuckPolls)
                    {
                        // Best-effort cancel of the server-side task so it doesn't leak until TTL expires.
                        try
                        {
                            await CancelTaskAsync(taskId, CancellationToken.None).ConfigureAwait(false);
                        }
                        catch
                        {
                            // Swallow secondary failures; we're already propagating an exception.
                        }

                        throw new McpException(
                            $"Task '{taskId}' has remained in '{McpTaskStatus.InputRequired}' for {maxConsecutiveStuckPolls} consecutive polls " +
                            "without publishing new input requests after all previously requested inputs were resolved.");
                    }

                    break;

                case WorkingTaskResult:
                    // Continue polling.
                    consecutiveStuckPolls = 0;
                    break;

                default:
                    throw new McpException(
                        $"Unexpected task result type '{taskResult.GetType().Name}' for task '{taskId}'.");
            }
        }
    }

    /// <summary>
    /// Invokes a tool on the server with task extension support, returning the raw response
    /// without automatic polling. The caller is responsible for handling task lifecycle.
    /// </summary>
    /// <param name="requestParams">The request parameters to send. The tasks extension capability will be injected into the request metadata.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests. The default is <see cref="CancellationToken.None"/>.</param>
    /// <returns>A <see cref="ResultOrCreatedTask{TResult}"/> that is either an immediate result or a task handle.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="requestParams"/> is <see langword="null"/>.</exception>
    /// <exception cref="McpException">The request failed or the server returned an error response.</exception>
    /// <remarks>
    /// <para>
    /// Unlike <see cref="CallToolAsync(CallToolRequestParams, CancellationToken)"/>, this method does not
    /// automatically poll for task completion. If the server returns a <see cref="CreateTaskResult"/>,
    /// the caller must manage polling via <see cref="GetTaskAsync(string, CancellationToken)"/>.
    /// </para>
    /// </remarks>
    public async ValueTask<ResultOrCreatedTask<CallToolResult>> CallToolRawAsync(
        CallToolRequestParams requestParams,
        CancellationToken cancellationToken = default)
    {
        Throw.IfNull(requestParams);

        var paramsWithMeta = new CallToolRequestParams
        {
            Name = requestParams.Name,
            Arguments = requestParams.Arguments,
            // The SEP-2663 Tasks extension requires the 2026-07-28 or later protocol revision. On an older session, send a plain tools/call
            // (no task capability envelope) so the server returns a direct CallToolResult and never
            // creates a task.
            Meta = IsJuly2026OrLaterProtocol() ? GetMetaWithTaskCapability(requestParams.Meta) : requestParams.Meta,
        };

        JsonRpcRequest jsonRpcRequest = new()
        {
            Method = RequestMethods.ToolsCall,
            Params = JsonSerializer.SerializeToNode(paramsWithMeta, McpJsonUtilities.JsonContext.Default.CallToolRequestParams),
        };

        JsonRpcResponse response = await SendRequestAsync(jsonRpcRequest, cancellationToken).ConfigureAwait(false);

        // Discriminate based on resultType field.
        if (response.Result is JsonObject resultObj &&
            resultObj.TryGetPropertyValue("resultType", out var resultTypeNode) &&
            resultTypeNode?.GetValue<string>() == "task")
        {
            var taskCreated = resultObj.Deserialize(McpJsonUtilities.JsonContext.Default.CreateTaskResult)
                ?? throw new JsonException("Failed to deserialize CreateTaskResult from response.");
            return new ResultOrCreatedTask<CallToolResult>(taskCreated);
        }

        var callToolResult = JsonSerializer.Deserialize(response.Result, McpJsonUtilities.JsonContext.Default.CallToolResult)
            ?? throw new JsonException("Failed to deserialize CallToolResult from response.");
        return new ResultOrCreatedTask<CallToolResult>(callToolResult);
    }

    /// <summary>
    /// Sets the logging level for the server to control which log messages are sent to the client.
    /// </summary>
    /// <param name="level">The minimum severity level of log messages to receive from the server.</param>
    /// <param name="options">Optional request options including metadata, serialization settings, and progress tracking.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests. The default is <see cref="CancellationToken.None"/>.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    /// <exception cref="McpException">The request failed or the server returned an error response.</exception>
    [Obsolete(Obsoletions.DeprecatedLogging_Message, DiagnosticId = Obsoletions.Deprecated_DiagnosticId, UrlFormat = Obsoletions.Deprecated_Url)]
    public Task SetLoggingLevelAsync(LogLevel level, RequestOptions? options = null, CancellationToken cancellationToken = default) =>
        SetLoggingLevelAsync(McpServerImpl.ToLoggingLevel(level), options, cancellationToken);

    /// <summary>
    /// Sets the logging level for the server to control which log messages are sent to the client.
    /// </summary>
    /// <param name="level">The minimum severity level of log messages to receive from the server.</param>
    /// <param name="options">Optional request options including metadata, serialization settings, and progress tracking.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests. The default is <see cref="CancellationToken.None"/>.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    /// <exception cref="McpException">The request failed or the server returned an error response.</exception>
    [Obsolete(Obsoletions.DeprecatedLogging_Message, DiagnosticId = Obsoletions.Deprecated_DiagnosticId, UrlFormat = Obsoletions.Deprecated_Url)]
    public Task SetLoggingLevelAsync(LoggingLevel level, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        return SetLoggingLevelAsync(
            new SetLevelRequestParams
            {
                Level = level, 
                Meta = options?.GetMetaForRequest()
            },
            cancellationToken);
    }

    /// <summary>
    /// Sets the logging level for the server to control which log messages are sent to the client.
    /// </summary>
    /// <param name="requestParams">The request parameters to send in the request.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests. The default is <see cref="CancellationToken.None"/>.</param>
    /// <returns>The result of the request.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="requestParams"/> is <see langword="null"/>.</exception>
    /// <exception cref="McpException">The request failed or the server returned an error response.</exception>
    [Obsolete(Obsoletions.DeprecatedLogging_Message, DiagnosticId = Obsoletions.Deprecated_DiagnosticId, UrlFormat = Obsoletions.Deprecated_Url)]
    public Task SetLoggingLevelAsync(
        SetLevelRequestParams requestParams,
        CancellationToken cancellationToken = default)
    {
        Throw.IfNull(requestParams);

        return SendRequestAsync(
            RequestMethods.LoggingSetLevel,
            requestParams,
            McpJsonUtilities.JsonContext.Default.SetLevelRequestParams,
            McpJsonUtilities.JsonContext.Default.EmptyResult,
            cancellationToken: cancellationToken).AsTask();
    }

    /// <summary>
    /// Retrieves the current state of a task from the server.
    /// </summary>
    /// <param name="taskId">The stable identifier of the task to retrieve.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests. The default is <see cref="CancellationToken.None"/>.</param>
    /// <returns>A <see cref="GetTaskResult"/> subtype representing the current task state.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="taskId"/> is <see langword="null"/>.</exception>
    /// <exception cref="McpException">The request failed or the server returned an error response.</exception>
    public ValueTask<GetTaskResult> GetTaskAsync(
        string taskId,
        CancellationToken cancellationToken = default)
    {
        Throw.IfNull(taskId);

        return GetTaskAsync(new GetTaskRequestParams { TaskId = taskId }, cancellationToken);
    }

    /// <summary>
    /// Retrieves the current state of a task from the server.
    /// </summary>
    /// <param name="requestParams">The request parameters to send in the request.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests. The default is <see cref="CancellationToken.None"/>.</param>
    /// <returns>A <see cref="GetTaskResult"/> subtype representing the current task state.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="requestParams"/> is <see langword="null"/>.</exception>
    /// <exception cref="McpException">The request failed or the server returned an error response.</exception>
    public ValueTask<GetTaskResult> GetTaskAsync(
        GetTaskRequestParams requestParams,
        CancellationToken cancellationToken = default)
    {
        Throw.IfNull(requestParams);
        ThrowIfTasksNotSupported(nameof(GetTaskAsync));

        return SendRequestAsync(
            RequestMethods.TasksGet,
            requestParams,
            McpJsonUtilities.JsonContext.Default.GetTaskRequestParams,
            McpJsonUtilities.JsonContext.Default.GetTaskResult,
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Provides input responses to a task that is in the <see cref="McpTaskStatus.InputRequired"/> state.
    /// </summary>
    /// <param name="requestParams">The request parameters containing the task ID and input responses.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests. The default is <see cref="CancellationToken.None"/>.</param>
    /// <returns>The result acknowledging the update.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="requestParams"/> is <see langword="null"/>.</exception>
    /// <exception cref="McpException">The request failed or the server returned an error response.</exception>
    public ValueTask<UpdateTaskResult> UpdateTaskAsync(
        UpdateTaskRequestParams requestParams,
        CancellationToken cancellationToken = default)
    {
        Throw.IfNull(requestParams);
        ThrowIfTasksNotSupported(nameof(UpdateTaskAsync));

        return SendRequestAsync(
            RequestMethods.TasksUpdate,
            requestParams,
            McpJsonUtilities.JsonContext.Default.UpdateTaskRequestParams,
            McpJsonUtilities.JsonContext.Default.UpdateTaskResult,
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Requests cancellation of an in-progress task on the server.
    /// </summary>
    /// <param name="taskId">The stable identifier of the task to cancel.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests. The default is <see cref="CancellationToken.None"/>.</param>
    /// <returns>The result acknowledging the cancellation request.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="taskId"/> is <see langword="null"/>.</exception>
    /// <exception cref="McpException">The request failed or the server returned an error response.</exception>
    public ValueTask<CancelTaskResult> CancelTaskAsync(
        string taskId,
        CancellationToken cancellationToken = default)
    {
        Throw.IfNull(taskId);

        return CancelTaskAsync(new CancelTaskRequestParams { TaskId = taskId }, cancellationToken);
    }

    /// <summary>
    /// Requests cancellation of an in-progress task on the server.
    /// </summary>
    /// <param name="requestParams">The request parameters to send in the request.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests. The default is <see cref="CancellationToken.None"/>.</param>
    /// <returns>The result acknowledging the cancellation request.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="requestParams"/> is <see langword="null"/>.</exception>
    /// <exception cref="McpException">The request failed or the server returned an error response.</exception>
    public ValueTask<CancelTaskResult> CancelTaskAsync(
        CancelTaskRequestParams requestParams,
        CancellationToken cancellationToken = default)
    {
        Throw.IfNull(requestParams);
        ThrowIfTasksNotSupported(nameof(CancelTaskAsync));

        return SendRequestAsync(
            RequestMethods.TasksCancel,
            requestParams,
            McpJsonUtilities.JsonContext.Default.CancelTaskRequestParams,
            McpJsonUtilities.JsonContext.Default.CancelTaskResult,
            cancellationToken: cancellationToken);
    }

    /// <summary>Converts a dictionary with <see cref="object"/> values to a dictionary with <see cref="JsonElement"/> values.</summary>
    private static Dictionary<string, JsonElement>? ToArgumentsDictionary(
        IReadOnlyDictionary<string, object?>? arguments, JsonSerializerOptions options)
    {
        var typeInfo = options.GetTypeInfo<object?>();

        Dictionary<string, JsonElement>? result = null;
        if (arguments is not null)
        {
            result = new(arguments.Count);
            foreach (var kvp in arguments)
            {
                result.Add(kvp.Key, kvp.Value is JsonElement je ? je : JsonSerializer.SerializeToElement(kvp.Value, typeInfo));
            }
        }

        return result;
    }

    // Per SEP-2663 §51, the per-request opt-in uses the SEP-2575 capabilities envelope:
    //   _meta/io.modelcontextprotocol/clientCapabilities/extensions/io.modelcontextprotocol/tasks = {}
    private static JsonObject GetMetaWithTaskCapability(JsonObject? existingMeta)
    {
        JsonObject meta = existingMeta is not null
            ? (JsonObject)existingMeta.DeepClone()
            : [];

        if (meta[MetaKeys.ClientCapabilities] is not JsonObject capsRoot)
        {
            capsRoot = [];
            meta[MetaKeys.ClientCapabilities] = capsRoot;
        }

        if (capsRoot["extensions"] is not JsonObject extensionsRoot)
        {
            extensionsRoot = [];
            capsRoot["extensions"] = extensionsRoot;
        }

        extensionsRoot.TryAdd(McpExtensions.Tasks, new JsonObject());
        return meta;
    }

    /// <summary>
    /// Throws when the negotiated protocol version does not support the SEP-2663 Tasks extension. Tasks
    /// require the 2026-07-28 or later protocol revision, and a task id only ever exists when the session
    /// negotiated such a revision, so invoking <c>tasks/get</c>, <c>tasks/update</c>, or <c>tasks/cancel</c>
    /// on an older session is a programming error rather than a recoverable protocol condition.
    /// </summary>
    private void ThrowIfTasksNotSupported(string operationName)
    {
        if (!IsJuly2026OrLaterProtocol())
        {
            throw new InvalidOperationException(
                $"'{operationName}' requires a newer protocol revision that supports tasks. " +
                $"The negotiated protocol version is '{NegotiatedProtocolVersion ?? "(none)"}'.");
        }
    }
}
