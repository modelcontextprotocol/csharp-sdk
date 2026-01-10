using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
#pragma warning disable MEAI001 // Type is for evaluation purposes only and is subject to change or removal in future updates.

namespace ModelContextProtocol.Client;

/// <summary>
/// Extension methods for adding MCP client support to chat clients.
/// </summary>
public static class McpChatClientBuilderExtensions
{
    /// <summary>
    /// Adds a chat client to the chat client pipeline that creates an <see cref="McpClient"/> for each <see cref="HostedMcpServerTool"/>
    /// in <see cref="ChatOptions.Tools"/> and augments it with the tools from MCP servers as <see cref="AIFunction"/> instances.
    /// </summary>
    /// <param name="builder">The <see cref="ChatClientBuilder"/> to configure.</param>
    /// <param name="httpClient">The <see cref="HttpClient"/> to use, or <see langword="null"/> to create a new instance.</param>
    /// <param name="loggerFactory">The <see cref="ILoggerFactory"/> to use, or <see langword="null"/> to resolve from services.</param>
    /// <param name="configureTransportOptions">An optional callback to configure the <see cref="HttpClientTransportOptions"/> for each <see cref="HostedMcpServerTool"/>.</param>
    /// <returns>The <see cref="ChatClientBuilder"/> for method chaining.</returns>
    /// <remarks>
    /// <para>
    /// When a <c>HostedMcpServerTool</c> is encountered in the tools collection, the client
    /// connects to the MCP server, retrieves available tools, and expands them into callable AI functions.
    /// Connections are cached by server address to avoid redundant connections.
    /// </para>
    /// <para>
    /// Use this method as an alternative when working with chat providers that don't have built-in support for hosted MCP servers.
    /// </para>
    /// </remarks>
    [Experimental(Experimentals.UseMcpClient_DiagnosticId)]
    public static ChatClientBuilder UseMcpClient(
        this ChatClientBuilder builder,
        HttpClient? httpClient = null,
        ILoggerFactory? loggerFactory = null,
        Action<HostedMcpServerTool, HttpClientTransportOptions>? configureTransportOptions = null)
    {
        return builder.Use((innerClient, services) =>
        {
            loggerFactory ??= (ILoggerFactory)services.GetService(typeof(ILoggerFactory))!;
            var chatClient = new McpChatClient(innerClient, httpClient, loggerFactory, configureTransportOptions);
            return chatClient;
        });
    }

    private sealed class McpChatClient : DelegatingChatClient
    {
        private readonly ILoggerFactory? _loggerFactory;
        private readonly ILogger _logger;
        private readonly HttpClient _httpClient;
        private readonly bool _ownsHttpClient;
        private readonly McpClientTasksLruCache _lruCache;
        private readonly Action<HostedMcpServerTool, HttpClientTransportOptions>? _configureTransportOptions;

        /// <summary>
        /// Initializes a new instance of the <see cref="McpChatClient"/> class.
        /// </summary>
        /// <param name="innerClient">The underlying <see cref="IChatClient"/>, or the next instance in a chain of clients.</param>
        /// <param name="httpClient">An optional <see cref="HttpClient"/> to use when connecting to MCP servers. If not provided, a new instance will be created.</param>
        /// <param name="loggerFactory">An <see cref="ILoggerFactory"/> to use for logging information about function invocation.</param>
        /// <param name="configureTransportOptions">An optional callback to configure the <see cref="HttpClientTransportOptions"/> for each <see cref="HostedMcpServerTool"/>.</param>
        public McpChatClient(IChatClient innerClient, HttpClient? httpClient = null, ILoggerFactory? loggerFactory = null, Action<HostedMcpServerTool, HttpClientTransportOptions>? configureTransportOptions = null)
            : base(innerClient)
        {
            _loggerFactory = loggerFactory;
            _logger = (ILogger?)loggerFactory?.CreateLogger<McpChatClient>() ?? NullLogger.Instance;
            _httpClient = httpClient ?? new HttpClient();
            _ownsHttpClient = httpClient is null;
            _lruCache = new McpClientTasksLruCache(capacity: 20);
            _configureTransportOptions = configureTransportOptions;
        }

        public override async Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            if (options?.Tools is { Count: > 0 })
            {
                var downstreamTools = await BuildDownstreamAIToolsAsync(options.Tools).ConfigureAwait(false);
                options = options.Clone();
                options.Tools = downstreamTools;
            }

            return await base.GetResponseAsync(messages, options, cancellationToken).ConfigureAwait(false);
        }

        public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            if (options?.Tools is { Count: > 0 })
            {
                var downstreamTools = await BuildDownstreamAIToolsAsync(options.Tools).ConfigureAwait(false);
                options = options.Clone();
                options.Tools = downstreamTools;
            }

            await foreach (var update in base.GetStreamingResponseAsync(messages, options, cancellationToken).ConfigureAwait(false))
            {
                yield return update;
            }
        }

        private async Task<List<AITool>> BuildDownstreamAIToolsAsync(IList<AITool> chatOptionsTools)
        {
            List<AITool> downstreamTools = [];
            foreach (var chatOptionsTool in chatOptionsTools)
            {
                if (chatOptionsTool is not HostedMcpServerTool hostedMcpTool)
                {
                    // For other tools, we want to keep them in the list of tools.
                    downstreamTools.Add(chatOptionsTool);
                    continue;
                }

                if (!Uri.TryCreate(hostedMcpTool.ServerAddress, UriKind.Absolute, out var parsedAddress) ||
                   (parsedAddress.Scheme != Uri.UriSchemeHttp && parsedAddress.Scheme != Uri.UriSchemeHttps))
                {
                   throw new InvalidOperationException(
                       $"Invalid http(s) address: '{hostedMcpTool.ServerAddress}'. MCP server address must be an absolute http(s) URL.");
                }

                // Get MCP client and its tools from cache (both are fetched together on first access).
                var (_, mcpTools) = await GetClientAndToolsAsync(hostedMcpTool, parsedAddress).ConfigureAwait(false);

                // Add the listed functions to our list of tools we'll pass to the inner client.
                foreach (var mcpTool in mcpTools)
                {
                    if (hostedMcpTool.AllowedTools is not null && !hostedMcpTool.AllowedTools.Contains(mcpTool.Name))
                    {
                        if (_logger.IsEnabled(LogLevel.Information))
                        {
                            _logger.LogInformation("MCP function '{FunctionName}' is not allowed by the tool configuration.", mcpTool.Name);
                        }
                        continue;
                    }

                    var wrappedFunction = new McpRetriableAIFunction(mcpTool, hostedMcpTool, parsedAddress, this);

                    switch (hostedMcpTool.ApprovalMode)
                    {
                        case HostedMcpServerToolNeverRequireApprovalMode:
                        case HostedMcpServerToolRequireSpecificApprovalMode specificApprovalMode when specificApprovalMode.NeverRequireApprovalToolNames?.Contains(mcpTool.Name) is true:
                            downstreamTools.Add(wrappedFunction);
                            break;

                        default:
                            // Default to always require approval if no specific mode is set.
                            downstreamTools.Add(new ApprovalRequiredAIFunction(wrappedFunction));
                            break;
                    }
                }
            }

            return downstreamTools;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_ownsHttpClient)
                {
                    _httpClient?.Dispose();
                }

                _lruCache.Dispose();
            }

            base.Dispose(disposing);
        }

        internal async Task<(McpClient Client, IList<McpClientTool> Tools)> GetClientAndToolsAsync(HostedMcpServerTool hostedMcpTool, Uri serverAddressUri)
        {
            // Note: We don't pass cancellationToken to the factory because the cached task should not be tied to any single caller's cancellation token.
            // Instead, callers can cancel waiting for the task, but the connection attempt itself will complete independently.
            Task<(McpClient, IList<McpClientTool> Tools)> task = _lruCache.GetOrAdd(
                hostedMcpTool.ServerAddress,
                static (_, state) => state.self.CreateMcpClientAndToolsAsync(state.hostedMcpTool, state.serverAddressUri, CancellationToken.None),
                (self: this, hostedMcpTool, serverAddressUri));

            try
            {
                return await task.ConfigureAwait(false);
            }
            catch
            {
                bool result = RemoveMcpClientFromCache(hostedMcpTool.ServerAddress, out var removedTask);
                Debug.Assert(result && removedTask!.Status != TaskStatus.RanToCompletion);
                throw;
            }
        }

        private async Task<(McpClient Client, IList<McpClientTool> Tools)> CreateMcpClientAndToolsAsync(HostedMcpServerTool hostedMcpTool, Uri serverAddressUri, CancellationToken cancellationToken)
        {
            var transportOptions = new HttpClientTransportOptions
            {
                Endpoint = serverAddressUri,
                Name = hostedMcpTool.ServerName,
                AdditionalHeaders = hostedMcpTool.AuthorizationToken is not null
                    // Update to pass all headers once https://github.com/dotnet/extensions/pull/7053 is available.
                    ? new Dictionary<string, string>() { { "Authorization", $"Bearer {hostedMcpTool.AuthorizationToken}" } }
                    : null,
            };

            _configureTransportOptions?.Invoke(new DummyHostedMcpServerTool(hostedMcpTool.ServerName, serverAddressUri), transportOptions);

            var transport = new HttpClientTransport(transportOptions, _httpClient, _loggerFactory);
            var client = await McpClient.CreateAsync(transport, cancellationToken: cancellationToken).ConfigureAwait(false);
            try
            {
                var tools = await client.ListToolsAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
                return (client, tools);
            }
            catch
            {
                try
                {
                    await client.DisposeAsync().ConfigureAwait(false);
                }
                catch { } // allow the original exception to propagate

                throw;
            }
        }

        internal bool RemoveMcpClientFromCache(string key, out Task<(McpClient Client, IList<McpClientTool> Tools)>? removedTask)
            => _lruCache.TryRemove(key, out removedTask);

        /// <summary>
        /// A temporary <see cref="HostedMcpServerTool"/> instance passed to the configureTransportOptions callback.
        /// This prevents the callback from modifying the original tool instance.
        /// </summary>
        private sealed class DummyHostedMcpServerTool(string serverName, Uri serverAddress)
            : HostedMcpServerTool(serverName, serverAddress);
    }

    /// <summary>
    /// An AI function wrapper that retries the invocation by recreating an MCP client when an <see cref="HttpRequestException"/> occurs.
    /// For example, this can happen if a session is revoked or a server error occurs. The retry evicts the cached MCP client.
    /// </summary>
    private sealed class McpRetriableAIFunction : DelegatingAIFunction
    {
        private readonly HostedMcpServerTool _hostedMcpTool;
        private readonly Uri _serverAddressUri;
        private readonly McpChatClient _chatClient;

        public McpRetriableAIFunction(AIFunction innerFunction, HostedMcpServerTool hostedMcpTool, Uri serverAddressUri, McpChatClient chatClient)
            : base(innerFunction)
        {
            _hostedMcpTool = hostedMcpTool;
            _serverAddressUri = serverAddressUri;
            _chatClient = chatClient;
        }

        protected override async ValueTask<object?> InvokeCoreAsync(AIFunctionArguments arguments, CancellationToken cancellationToken)
        {
            try
            {
                return await base.InvokeCoreAsync(arguments, cancellationToken).ConfigureAwait(false);
            }
            catch (HttpRequestException) { }

            bool result = _chatClient.RemoveMcpClientFromCache(_hostedMcpTool.ServerAddress, out var removedTask);
            Debug.Assert(result && removedTask!.Status == TaskStatus.RanToCompletion);
            _ = removedTask!.Result.Client.DisposeAsync().AsTask();

            var freshTool = await GetCurrentToolAsync().ConfigureAwait(false);
            return await freshTool.InvokeAsync(arguments, cancellationToken).ConfigureAwait(false);
        }

        private async Task<AIFunction> GetCurrentToolAsync()
        {
            Debug.Assert(Uri.TryCreate(_hostedMcpTool.ServerAddress, UriKind.Absolute, out var parsedAddress) &&
                        (parsedAddress.Scheme == Uri.UriSchemeHttp || parsedAddress.Scheme == Uri.UriSchemeHttps),
                        "Server address should have been validated before construction");

            var (_, tools) = await _chatClient.GetClientAndToolsAsync(_hostedMcpTool, _serverAddressUri!).ConfigureAwait(false);

            return tools.FirstOrDefault(t => t.Name == Name) ??
                throw new McpProtocolException($"Tool '{Name}' no longer exists on the MCP server.", McpErrorCode.InvalidParams);
        }
    }
}
