using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Logging;
using ModelContextProtocol.Utils;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously

namespace ModelContextProtocol.Protocol.Transport;

/// <summary>
/// Provides a <see cref="IClientTransport"/> implemented via "stdio" (standard input/output).
/// </summary>
/// <remarks>
/// <para>
/// This transport launches an external process and communicates with it through standard input and output streams.
/// It's used to connect to MCP servers launched and hosted in child processes.
/// </para>
/// <para>
/// The transport manages the entire lifecycle of the process: starting it with specified command-line arguments
/// and environment variables, handling output, and properly terminating the process when the transport is closed.
/// </para>
/// </remarks>
public sealed class StdioClientTransport : IClientTransport
{
    private readonly StdioClientTransportOptions _options;
    private readonly ILoggerFactory? _loggerFactory;

    /// <summary>
    /// Initializes a new instance of the <see cref="StdioClientTransport"/> class.
    /// </summary>
    /// <param name="options">Configuration options for the transport, including the command to execute, arguments, working directory, and environment variables.</param>
    /// <param name="loggerFactory">Logger factory for creating loggers used for diagnostic output during transport operations.</param>
    public StdioClientTransport(StdioClientTransportOptions options, ILoggerFactory? loggerFactory = null)
    {
        Throw.IfNull(options);

        _options = options;
        _loggerFactory = loggerFactory;
        Name = options.Name ?? $"stdio-{Regex.Replace(Path.GetFileName(options.Command), @"[\s\.]+", "-")}";
    }

    /// <inheritdoc />
    public string Name { get; }

    /// <inheritdoc />
    public async Task<ITransport> ConnectAsync(CancellationToken cancellationToken = default)
    {
        string endpointName = Name;

        Process? process = null;
        bool processStarted = false;

        string command = _options.Command;
        IList<string>? arguments = _options.Arguments;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) &&
            !string.Equals(Path.GetFileName(command), "cmd.exe", StringComparison.OrdinalIgnoreCase))
        {
            // On Windows, for stdio, we need to wrap non-shell commands with cmd.exe /c {command} (usually npx or uvicorn).
            // The stdio transport will not work correctly if the command is not run in a shell.
            arguments = arguments is null or [] ? ["/c", command] : ["/c", command, ..arguments];
            command = "cmd.exe";
        }

        ILogger logger = (ILogger?)_loggerFactory?.CreateLogger<StdioClientTransport>() ?? NullLogger.Instance;
        try
        {
            logger.TransportConnecting(endpointName);

            UTF8Encoding noBomUTF8 = new(encoderShouldEmitUTF8Identifier: false);

            ProcessStartInfo startInfo = new()
            {
                FileName = command,
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

            if (arguments is not null) 
            {
#if NET
                foreach (string arg in arguments)
                {
                    startInfo.ArgumentList.Add(arg);
                }
#else
                StringBuilder argsBuilder = new();
                foreach (string arg in arguments)
                {
                    PasteArguments.AppendArgument(argsBuilder, arg);
                }

                startInfo.Arguments = argsBuilder.ToString();
#endif
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
        Process? process, bool processRunning, ILogger logger, TimeSpan shutdownTimeout, string endpointName)
    {
        if (process is not null)
        {
            if (processRunning)
            {
                try
                {
                    processRunning = !process.HasExited;
                }
                catch
                {
                    processRunning = false;
                }
            }

            try
            {
                if (processRunning)
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
