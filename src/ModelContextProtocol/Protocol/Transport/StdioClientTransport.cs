using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Logging;
using ModelContextProtocol.Utils;
using System.Diagnostics;
using System.Text;

#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously

namespace ModelContextProtocol.Protocol.Transport;

/// <summary>
/// Provides a client MCP transport implemented via "stdio" (standard input/output).
/// </summary>
public sealed class StdioClientTransport : IClientTransport
{
    private readonly StdioClientTransportOptions _options;
    private readonly McpServerConfig _serverConfig;
    private readonly ILoggerFactory? _loggerFactory;

    /// <summary>
    /// Initializes a new instance of the <see cref="StdioClientTransport"/> class.
    /// </summary>
    /// <param name="options">Configuration options for the transport.</param>
    /// <param name="serverConfig">The server configuration for the transport.</param>
    /// <param name="loggerFactory">A logger factory for creating loggers.</param>
    public StdioClientTransport(StdioClientTransportOptions options, McpServerConfig serverConfig, ILoggerFactory? loggerFactory = null)
    {
        Throw.IfNull(options);
        Throw.IfNull(serverConfig);

        _options = options;
        _serverConfig = serverConfig;
        _loggerFactory = loggerFactory;
    }

    /// <inheritdoc />
    public async Task<ITransport> ConnectAsync(CancellationToken cancellationToken = default)
    {
        string endpointName = $"Client (stdio) for ({_serverConfig.Id}: {_serverConfig.Name})";

        Process? process = null;
        bool processStarted = false;

        ILogger logger = (ILogger?)_loggerFactory?.CreateLogger<StdioClientTransport>() ?? NullLogger.Instance;
        try
        {
            logger.TransportConnecting(endpointName);

            UTF8Encoding noBomUTF8 = new(encoderShouldEmitUTF8Identifier: false);

            ProcessStartInfo startInfo = new()
            {
                FileName = _options.Command,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = _options.WorkingDirectory ?? Environment.CurrentDirectory,
                StandardOutputEncoding = noBomUTF8,
                StandardErrorEncoding = noBomUTF8,
#if NET
                StandardInputEncoding = noBomUTF8,
#endif
            };

            if (!string.IsNullOrWhiteSpace(_options.Arguments))
            {
                startInfo.Arguments = _options.Arguments;
            }

            if (_options.EnvironmentVariables != null)
            {
                foreach (var entry in _options.EnvironmentVariables)
                {
                    startInfo.Environment[entry.Key] = entry.Value;
                }
            }

            logger.CreateProcessForTransport(endpointName, _options.Command,
                startInfo.Arguments, string.Join(", ", startInfo.Environment.Select(kvp => kvp.Key + "=" + kvp.Value)),
                startInfo.WorkingDirectory, _options.ShutdownTimeout.ToString());

            process = new() { StartInfo = startInfo };

            // Set up error logging
            process.ErrorDataReceived += (sender, args) => logger.ReadStderr(endpointName, args.Data ?? "(no data)");

            // We need both stdin and stdout to use a no-BOM UTF-8 encoding. On .NET Core,
            // we can use ProcessStartInfo.StandardOutputEncoding/StandardInputEncoding, but
            // StandardInputEncoding doesn't exist on .NET Framework; instead, it always picks
            // up the encoding from Console.InputEncoding. As such, when not targeting .NET Core,
            // we temporarily change Console.InputEncoding to no-BOM UTF-8 around the Process.Start
            // call, to ensure it picks up the correct encoding.
#if NET
            processStarted = process.Start();
#else
            Encoding originalInputEncoding = Console.InputEncoding;
            try
            {
                Console.InputEncoding = noBomUTF8;
                processStarted = process.Start();
            }
            finally
            {
                Console.InputEncoding = originalInputEncoding;
            }
#endif

            if (!processStarted)
            {
                logger.TransportProcessStartFailed(endpointName);
                throw new McpTransportException("Failed to start MCP server process");
            }

            logger.TransportProcessStarted(endpointName, process.Id);

            process.BeginErrorReadLine();

            return new StdioClientSessionTransport(_options, process, endpointName, _loggerFactory);
        }
        catch (Exception ex)
        {
            logger.TransportConnectFailed(endpointName, ex);
            DisposeProcess(process, processStarted, logger, _options.ShutdownTimeout, endpointName);
            throw new McpTransportException("Failed to connect transport", ex);
        }
    }

    internal static void DisposeProcess(
        Process? process, bool processStarted, ILogger logger, TimeSpan shutdownTimeout, string endpointName)
    {
        if (process is not null)
        {
            try
            {
                if (processStarted && !process.HasExited)
                {
                    // Wait for the process to exit.
                    // Kill the while process tree because the process may spawn child processes
                    // and Node.js does not kill its children when it exits properly.
                    logger.TransportWaitingForShutdown(endpointName);
                    process.KillTree(shutdownTimeout);
                }
            }
            catch (Exception ex)
            {
                logger.TransportShutdownFailed(endpointName, ex);
            }
            finally
            {
                process.Dispose();
            }
        }
    }
}
