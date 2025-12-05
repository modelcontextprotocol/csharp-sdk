using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
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
    /// <exception cref="ArgumentNullException"><paramref name="clientTransport"/> or <paramref name="clientOptions"/> is <see langword="null"/>.</exception>
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
        catch
        {
            try
            {
                await clientSession.DisposeAsync().ConfigureAwait(false);
            }
            catch { } // allow the original exception to propagate

            throw;
        }

        return clientSession;
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
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="clientTransport"/> or <paramref name="resumeOptions"/> is <see langword="null"/>.</exception>
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
                Meta = options?.Meta
            },
            cancellationToken);
    }

    /// <summary>
    /// Sends a ping request to verify server connectivity.
    /// </summary>
    /// <param name="requestParams">The request parameters to send in the request.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests. The default is <see cref="CancellationToken.None"/>.</param>
    /// <returns>A task containing the ping result.</returns>
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
    public async ValueTask<IList<McpClientTool>> ListToolsAsync(
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        List<McpClientTool>? tools = null;
        ListToolsRequestParams requestParams = new() { Meta = options?.Meta };
        do
        {
            var toolResults = await ListToolsAsync(requestParams, cancellationToken).ConfigureAwait(false);
            tools ??= new(toolResults.Tools.Count);
            foreach (var tool in toolResults.Tools)
            {
                tools.Add(new(this, tool, options?.JsonSerializerOptions));
            }

            requestParams.Cursor = toolResults.NextCursor;
        }
        while (requestParams.Cursor is not null);

        return tools;
    }

    /// <summary>
    /// Retrieves a list of available tools from the server.
    /// </summary>
    /// <param name="requestParams">The request parameters to send in the request.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests. The default is <see cref="CancellationToken.None"/>.</param>
    /// <returns>The result of the request as provided by the server.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="requestParams"/> is <see langword="null"/>.</exception>
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

        return SendRequestAsync(
            RequestMethods.ToolsList,
            requestParams,
            McpJsonUtilities.JsonContext.Default.ListToolsRequestParams,
            McpJsonUtilities.JsonContext.Default.ListToolsResult,
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Retrieves a list of available prompts from the server.
    /// </summary>
    /// <param name="options">Optional request options including metadata, serialization settings, and progress tracking.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests. The default is <see cref="CancellationToken.None"/>.</param>
    /// <returns>A list of all available prompts as <see cref="McpClientPrompt"/> instances.</returns>
    public async ValueTask<IList<McpClientPrompt>> ListPromptsAsync(
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        List<McpClientPrompt>? prompts = null;
        ListPromptsRequestParams requestParams = new() { Meta = options?.Meta };
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

        return SendRequestAsync(
            RequestMethods.PromptsList,
            requestParams,
            McpJsonUtilities.JsonContext.Default.ListPromptsRequestParams,
            McpJsonUtilities.JsonContext.Default.ListPromptsResult,
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Retrieves a specific prompt from the MCP server.
    /// </summary>
    /// <param name="name">The name of the prompt to retrieve.</param>
    /// <param name="arguments">Optional arguments for the prompt. The dictionary keys are parameter names, and the values are the argument values.</param>
    /// <param name="options">Optional request options including metadata, serialization settings, and progress tracking.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests. The default is <see cref="CancellationToken.None"/>.</param>
    /// <returns>A task containing the prompt's result with content and messages.</returns>
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
                Meta = options?.Meta,
            },
            cancellationToken);
    }

    /// <summary>
    /// Retrieves a list of available prompts from the server.
    /// </summary>
    /// <param name="requestParams">The request parameters to send in the request.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests. The default is <see cref="CancellationToken.None"/>.</param>
    /// <returns>The result of the request as provided by the server.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="requestParams"/> is <see langword="null"/>.</exception>
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
    public async ValueTask<IList<McpClientResourceTemplate>> ListResourceTemplatesAsync(
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        List<McpClientResourceTemplate>? resourceTemplates = null;
        ListResourceTemplatesRequestParams requestParams = new() { Meta = options?.Meta };
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

        return SendRequestAsync(
            RequestMethods.ResourcesTemplatesList,
            requestParams,
            McpJsonUtilities.JsonContext.Default.ListResourceTemplatesRequestParams,
            McpJsonUtilities.JsonContext.Default.ListResourceTemplatesResult,
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Retrieves a list of available resources from the server.
    /// </summary>
    /// <param name="options">Optional request options including metadata, serialization settings, and progress tracking.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests. The default is <see cref="CancellationToken.None"/>.</param>
    /// <returns>A list of all available resources as <see cref="Resource"/> instances.</returns>
    public async ValueTask<IList<McpClientResource>> ListResourcesAsync(
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        List<McpClientResource>? resources = null;
        ListResourcesRequestParams requestParams = new() { Meta = options?.Meta };
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

        return SendRequestAsync(
            RequestMethods.ResourcesList,
            requestParams,
            McpJsonUtilities.JsonContext.Default.ListResourcesRequestParams,
            McpJsonUtilities.JsonContext.Default.ListResourcesResult,
            cancellationToken: cancellationToken);
    }
        
    /// <summary>
    /// Reads a resource from the server.
    /// </summary>
    /// <param name="uri">The URI of the resource.</param>
    /// <param name="options">Optional request options including metadata, serialization settings, and progress tracking.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests. The default is <see cref="CancellationToken.None"/>.</param>
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
    public ValueTask<ReadResourceResult> ReadResourceAsync(
        string uri, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        Throw.IfNullOrWhiteSpace(uri);

        return ReadResourceAsync(new ReadResourceRequestParams
        {
            Uri = uri,
            Meta = options?.Meta,
        }, cancellationToken);
    }

    /// <summary>
    /// Reads a resource from the server.
    /// </summary>
    /// <param name="uriTemplate">The URI template of the resource.</param>
    /// <param name="arguments">Arguments to use to format <paramref name="uriTemplate"/>.</param>
    /// <param name="options">Optional request options including metadata, serialization settings, and progress tracking.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests. The default is <see cref="CancellationToken.None"/>.</param>
    public ValueTask<ReadResourceResult> ReadResourceAsync(
        string uriTemplate, IReadOnlyDictionary<string, object?> arguments, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        Throw.IfNullOrWhiteSpace(uriTemplate);
        Throw.IfNull(arguments);

        return ReadResourceAsync(
            new ReadResourceRequestParams 
            {
                Uri = UriTemplate.FormatUri(uriTemplate, arguments),
                Meta = options?.Meta,
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
    public ValueTask<ReadResourceResult> ReadResourceAsync(
        ReadResourceRequestParams requestParams,
        CancellationToken cancellationToken = default)
    {
        Throw.IfNull(requestParams);

        return SendRequestAsync(
            RequestMethods.ResourcesRead,
            requestParams,
            McpJsonUtilities.JsonContext.Default.ReadResourceRequestParams,
            McpJsonUtilities.JsonContext.Default.ReadResourceResult,
            cancellationToken: cancellationToken);
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
                Meta = options?.Meta,
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
    /// Unsubscribes from a resource on the server to stop receiving notifications about its changes.
    /// </summary>
    /// <param name="uri">The URI of the resource to which to subscribe.</param>
    /// <param name="options">Optional request options including metadata, serialization settings, and progress tracking.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests. The default is <see cref="CancellationToken.None"/>.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
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
    public Task SubscribeToResourceAsync(string uri, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        Throw.IfNullOrWhiteSpace(uri);

        return SubscribeToResourceAsync(
            new SubscribeRequestParams
            {
                Uri = uri,
                Meta = options?.Meta,
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
    /// Unsubscribes from a resource on the server to stop receiving notifications about its changes.
    /// </summary>
    /// <param name="uri">The URI of the resource to unsubscribe from.</param>
    /// <param name="options">Optional request options including metadata, serialization settings, and progress tracking.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests. The default is <see cref="CancellationToken.None"/>.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
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
    public Task UnsubscribeFromResourceAsync(string uri, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        Throw.IfNullOrWhiteSpace(uri);

        return UnsubscribeFromResourceAsync(
            new UnsubscribeRequestParams 
            {
                Uri = uri,
                Meta = options?.Meta
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
                    Meta = options?.Meta,
                },
                cancellationToken);
        }

        return SendRequestWithProgressAsync(toolName, arguments, progress, options?.Meta, serializerOptions, cancellationToken);

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

            JsonObject metaWithProgress = meta is not null ? new(meta) : [];
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
    public ValueTask<CallToolResult> CallToolAsync(
        CallToolRequestParams requestParams,
        CancellationToken cancellationToken = default)
    {
        Throw.IfNull(requestParams);

        return SendRequestAsync(
            RequestMethods.ToolsCall,
            requestParams,
            McpJsonUtilities.JsonContext.Default.CallToolRequestParams,
            McpJsonUtilities.JsonContext.Default.CallToolResult,
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Sets the logging level for the server to control which log messages are sent to the client.
    /// </summary>
    /// <param name="level">The minimum severity level of log messages to receive from the server.</param>
    /// <param name="options">Optional request options including metadata, serialization settings, and progress tracking.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests. The default is <see cref="CancellationToken.None"/>.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public Task SetLoggingLevelAsync(LogLevel level, RequestOptions? options = null, CancellationToken cancellationToken = default) =>
        SetLoggingLevelAsync(McpServerImpl.ToLoggingLevel(level), options, cancellationToken);

    /// <summary>
    /// Sets the logging level for the server to control which log messages are sent to the client.
    /// </summary>
    /// <param name="level">The minimum severity level of log messages to receive from the server.</param>
    /// <param name="options">Optional request options including metadata, serialization settings, and progress tracking.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests. The default is <see cref="CancellationToken.None"/>.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public Task SetLoggingLevelAsync(LoggingLevel level, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        return SetLoggingLevelAsync(
            new SetLevelRequestParams
            {
                Level = level, 
                Meta = options?.Meta
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
}
