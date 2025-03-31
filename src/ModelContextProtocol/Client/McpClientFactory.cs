using System.Globalization;
using System.Runtime.InteropServices;
using ModelContextProtocol.Logging;
using ModelContextProtocol.Protocol.Transport;
using ModelContextProtocol.Utils;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Reflection;

namespace ModelContextProtocol.Client;

/// <summary>Provides factory methods for creating MCP clients.</summary>
public static class McpClientFactory
{
    /// <summary>Default client options to use when none are supplied.</summary>
    private static readonly McpClientOptions s_defaultClientOptions = CreateDefaultClientOptions();

    /// <summary>Creates default client options to use when no options are supplied.</summary>
    private static McpClientOptions CreateDefaultClientOptions()
    {
        var asmName = (Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly()).GetName();
        return new()
        {
            ClientInfo = new()
            {
                Name = asmName.Name ?? "McpClient",
                Version = asmName.Version?.ToString() ?? "1.0.0",
            },
        };
    }

    /// <summary>Creates an <see cref="IMcpClient"/>, connecting it to the specified server.</summary>
    /// <param name="serverConfig">Configuration for the target server to which the client should connect.</param>
    /// <param name="clientOptions">
    /// A client configuration object which specifies client capabilities and protocol version.
    /// If <see langword="null"/>, details based on the current process will be employed.
    /// </param>
    /// <param name="createTransportFunc">An optional factory method which returns transport implementations based on a server configuration.</param>
    /// <param name="loggerFactory">A logger factory for creating loggers for clients.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>An <see cref="IMcpClient"/> that's connected to the specified server.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="serverConfig"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="clientOptions"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="serverConfig"/> contains invalid information.</exception>
    /// <exception cref="InvalidOperationException"><paramref name="createTransportFunc"/> returns an invalid transport.</exception>
    public static async Task<IMcpClient> CreateAsync(
        McpServerConfig serverConfig,
        McpClientOptions? clientOptions = null,
        Func<McpServerConfig, ILoggerFactory?, IClientTransport>? createTransportFunc = null,
        ILoggerFactory? loggerFactory = null,
        CancellationToken cancellationToken = default)
    {
        Throw.IfNull(serverConfig);

        clientOptions ??= s_defaultClientOptions;
        createTransportFunc ??= CreateTransport;
        
        string endpointName = $"Client ({serverConfig.Id}: {serverConfig.Name})";

        var logger = loggerFactory?.CreateLogger(typeof(McpClientFactory)) ?? NullLogger.Instance;
        logger.CreatingClient(endpointName);

        var transport = 
            createTransportFunc(serverConfig, loggerFactory) ??
            throw new InvalidOperationException($"{nameof(createTransportFunc)} returned a null transport.");

        try
        {
            McpClient client = new(transport, clientOptions, serverConfig, loggerFactory);
            try
            {
                await client.ConnectAsync(cancellationToken).ConfigureAwait(false);
                logger.ClientCreated(endpointName);
                return client;
            }
            catch
            {
                await client.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }
        catch
        {
            if (transport is IAsyncDisposable asyncDisposableTransport)
            {
                await asyncDisposableTransport.DisposeAsync().ConfigureAwait(false);
            }
            else if (transport is IDisposable disposableTransport)
            {
                disposableTransport.Dispose();
            }
            throw;
        }
    }

    private static IClientTransport CreateTransport(McpServerConfig serverConfig, ILoggerFactory? loggerFactory)
    {
        if (string.Equals(serverConfig.TransportType, TransportTypes.StdIo, StringComparison.OrdinalIgnoreCase))
        {
            string? command = serverConfig.TransportOptions?.GetValueOrDefault("command");
            if (string.IsNullOrWhiteSpace(command))
            {
                command = serverConfig.Location;
                if (string.IsNullOrWhiteSpace(command))
                {
                    throw new ArgumentException("Command is required for stdio transport.", nameof(serverConfig));
                }
            }

            string? arguments = serverConfig.TransportOptions?.GetValueOrDefault("arguments");

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) &&
                serverConfig.TransportType.Equals(TransportTypes.StdIo, StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrEmpty(command) &&
                !string.Equals(Path.GetFileName(command), "cmd.exe", StringComparison.OrdinalIgnoreCase))
            {
                // On Windows, for stdio, we need to wrap non-shell commands with cmd.exe /c {command} (usually npx or uvicorn).
                // The stdio transport will not work correctly if the command is not run in a shell.
                arguments = string.IsNullOrWhiteSpace(arguments) ? 
                    $"/c {command}" :
                    $"/c {command} {arguments}";
                command = "cmd.exe";
            }

            return new StdioClientTransport(new StdioClientTransportOptions
            {
                Command = command!,
                Arguments = arguments,
                WorkingDirectory = serverConfig.TransportOptions?.GetValueOrDefault("workingDirectory"),
                EnvironmentVariables = serverConfig.TransportOptions?
                    .Where(kv => kv.Key.StartsWith("env:", StringComparison.Ordinal))
                    .ToDictionary(kv => kv.Key.Substring("env:".Length), kv => kv.Value),
                ShutdownTimeout = TimeSpan.TryParse(serverConfig.TransportOptions?.GetValueOrDefault("shutdownTimeout"), CultureInfo.InvariantCulture, out var timespan) ? timespan : StdioClientTransportOptions.DefaultShutdownTimeout
            }, serverConfig, loggerFactory);
        }

        if (string.Equals(serverConfig.TransportType, TransportTypes.Sse, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(serverConfig.TransportType, "http", StringComparison.OrdinalIgnoreCase))
        {
            return new SseClientTransport(new SseClientTransportOptions
            {
                ConnectionTimeout = TimeSpan.FromSeconds(ParseInt32OrDefault(serverConfig.TransportOptions, "connectionTimeout", 30)),
                MaxReconnectAttempts = ParseInt32OrDefault(serverConfig.TransportOptions, "maxReconnectAttempts", 3),
                ReconnectDelay = TimeSpan.FromSeconds(ParseInt32OrDefault(serverConfig.TransportOptions, "reconnectDelay", 5)),
                AdditionalHeaders = serverConfig.TransportOptions?
                    .Where(kv => kv.Key.StartsWith("header.", StringComparison.Ordinal))
                    .ToDictionary(kv => kv.Key.Substring("header.".Length), kv => kv.Value)
            }, serverConfig, loggerFactory);

            static int ParseInt32OrDefault(Dictionary<string, string>? options, string key, int defaultValue) =>
                options?.TryGetValue(key, out var value) is not true ? defaultValue :
                int.TryParse(value, out var result) ? result :
                throw new ArgumentException($"Invalid value '{value}' for option '{key}' in transport options.", nameof(serverConfig));
        }

        throw new ArgumentException($"Unsupported transport type '{serverConfig.TransportType}'.", nameof(serverConfig));
    }
}