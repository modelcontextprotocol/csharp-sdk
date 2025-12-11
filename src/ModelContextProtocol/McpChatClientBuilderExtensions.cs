using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Client;
#pragma warning disable MEAI001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.

namespace ModelContextProtocol;

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
    public static ChatClientBuilder UseMcpClient(
        this ChatClientBuilder builder,
        HttpClient? httpClient = null,
        ILoggerFactory? loggerFactory = null)
    {
        return builder.Use((innerClient, services) =>
        {
            loggerFactory ??= (ILoggerFactory)services.GetService(typeof(ILoggerFactory))!;
            var chatClient = new McpChatClient(innerClient, httpClient, loggerFactory);
            return chatClient;
        });
    }

    private class McpChatClient : DelegatingChatClient
    {
        private readonly ILoggerFactory? _loggerFactory;
        private readonly ILogger _logger;
        private readonly HttpClient _httpClient;
        private readonly bool _ownsHttpClient;
        private ConcurrentDictionary<string, Task<McpClient>>? _mcpClientTasks = null;

        /// <summary>
        /// Initializes a new instance of the <see cref="McpChatClient"/> class.
        /// </summary>
        /// <param name="innerClient">The underlying <see cref="IChatClient"/>, or the next instance in a chain of clients.</param>
        /// <param name="httpClient">An optional <see cref="HttpClient"/> to use when connecting to MCP servers. If not provided, a new instance will be created.</param>
        /// <param name="loggerFactory">An <see cref="ILoggerFactory"/> to use for logging information about function invocation.</param>
        public McpChatClient(IChatClient innerClient, HttpClient? httpClient = null, ILoggerFactory? loggerFactory = null)
            : base(innerClient)
        {
            _loggerFactory = loggerFactory;
            _logger = (ILogger?)loggerFactory?.CreateLogger<McpChatClient>() ?? NullLogger.Instance;
            _httpClient = httpClient ?? new HttpClient();
            _ownsHttpClient = httpClient is null;
        }

        /// <inheritdoc/>
        public override async Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            if (options?.Tools is { Count: > 0 })
            {
                var downstreamTools = await BuildDownstreamAIToolsAsync(options.Tools, cancellationToken).ConfigureAwait(false);
                options = options.Clone();
                options.Tools = downstreamTools;
            }

            return await base.GetResponseAsync(messages, options, cancellationToken).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            if (options?.Tools is { Count: > 0 })
            {
                var downstreamTools = await BuildDownstreamAIToolsAsync(options.Tools, cancellationToken).ConfigureAwait(false);
                options = options.Clone();
                options.Tools = downstreamTools;
            }

            await foreach (var update in base.GetStreamingResponseAsync(messages, options, cancellationToken).ConfigureAwait(false))
            {
                yield return update;
            }
        }

        private async Task<List<AITool>?> BuildDownstreamAIToolsAsync(IList<AITool>? inputTools, CancellationToken cancellationToken)
        {
            List<AITool>? downstreamTools = null;
            foreach (var tool in inputTools ?? [])
            {
                if (tool is not HostedMcpServerTool mcpTool)
                {
                    // For other tools, we want to keep them in the list of tools.
                    downstreamTools ??= new List<AITool>();
                    downstreamTools.Add(tool);
                    continue;
                }

                if (!Uri.TryCreate(mcpTool.ServerAddress, UriKind.Absolute, out var parsedAddress) ||
                    (parsedAddress.Scheme != Uri.UriSchemeHttp && parsedAddress.Scheme != Uri.UriSchemeHttps))
                {
                    throw new InvalidOperationException(
                        $"MCP server address must be an absolute HTTP or HTTPS URI. Invalid address: '{mcpTool.ServerAddress}'");
                }

                // List all MCP functions from the specified MCP server.
                // This will need some caching in a real-world scenario to avoid repeated calls.
                var mcpClient = await CreateMcpClientAsync(parsedAddress, mcpTool.ServerName, mcpTool.AuthorizationToken).ConfigureAwait(false);
                var mcpFunctions = await mcpClient.ListToolsAsync(cancellationToken: cancellationToken).ConfigureAwait(false);

                // Add the listed functions to our list of tools we'll pass to the inner client.
                foreach (var mcpFunction in mcpFunctions)
                {
                    if (mcpTool.AllowedTools is not null && !mcpTool.AllowedTools.Contains(mcpFunction.Name))
                    {
                        _logger.LogInformation("MCP function '{FunctionName}' is not allowed by the tool configuration.", mcpFunction.Name);
                        continue;
                    }

                    downstreamTools ??= new List<AITool>();
                    switch (mcpTool.ApprovalMode)
                    {
                        case HostedMcpServerToolAlwaysRequireApprovalMode alwaysRequireApproval:
                            downstreamTools.Add(new ApprovalRequiredAIFunction(mcpFunction));
                            break;
                        case HostedMcpServerToolNeverRequireApprovalMode neverRequireApproval:
                            downstreamTools.Add(mcpFunction);
                            break;
                        case HostedMcpServerToolRequireSpecificApprovalMode specificApprovalMode when specificApprovalMode.AlwaysRequireApprovalToolNames?.Contains(mcpFunction.Name) is true:
                            downstreamTools.Add(new ApprovalRequiredAIFunction(mcpFunction));
                            break;
                        case HostedMcpServerToolRequireSpecificApprovalMode specificApprovalMode when specificApprovalMode.NeverRequireApprovalToolNames?.Contains(mcpFunction.Name) is true:
                            downstreamTools.Add(mcpFunction);
                            break;
                        default:
                            // Default to always require approval if no specific mode is set.
                            downstreamTools.Add(new ApprovalRequiredAIFunction(mcpFunction));
                            break;
                    }
                }
            }

            return downstreamTools;
        }

        /// <inheritdoc/>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                // Dispose of the HTTP client if it was created by this client.
                if (_ownsHttpClient)
                {
                    _httpClient?.Dispose();
                }

                if (_mcpClientTasks is not null)
                {
                    // Dispose of all cached MCP clients.
                    foreach (var clientTask in _mcpClientTasks.Values)
                    {
#if NETSTANDARD2_0
                        if (clientTask.Status == TaskStatus.RanToCompletion)
#else
                        if (clientTask.IsCompletedSuccessfully)
#endif
                        {
                            _ = clientTask.Result.DisposeAsync();
                        }
                    }

                    _mcpClientTasks.Clear();
                }
            }

            base.Dispose(disposing);
        }

        private Task<McpClient> CreateMcpClientAsync(Uri serverAddress, string serverName, string? authorizationToken)
        {
            if (_mcpClientTasks is null)
            {
                _mcpClientTasks = new ConcurrentDictionary<string, Task<McpClient>>(StringComparer.OrdinalIgnoreCase);
            }

            // Note: We don't pass cancellationToken to the factory because the cached task should not be tied to any single caller's cancellation token.
            // Instead, callers can cancel waiting for the task, but the connection attempt itself will complete independently.
            return _mcpClientTasks.GetOrAdd(serverAddress.ToString(), _ => CreateMcpClientCoreAsync(serverAddress, serverName, authorizationToken, CancellationToken.None));
        }

        private async Task<McpClient> CreateMcpClientCoreAsync(Uri serverAddress, string serverName, string? authorizationToken, CancellationToken cancellationToken)
        {
            var serverAddressKey = serverAddress.ToString();
            try
            {
                var transport = new HttpClientTransport(new HttpClientTransportOptions
                {
                    Endpoint = serverAddress,
                    Name = serverName,
                    AdditionalHeaders = authorizationToken is not null
                        // Update to pass all headers once https://github.com/dotnet/extensions/pull/7053 is available.
                        ? new Dictionary<string, string>() { { "Authorization", $"Bearer {authorizationToken}" } }
                        : null,
                }, _httpClient, _loggerFactory);

                return await McpClient.CreateAsync(transport, cancellationToken: cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                // Remove the failed task from cache so subsequent requests can retry
                _mcpClientTasks?.TryRemove(serverAddressKey, out _);
                throw;
            }
        }
    }
}
