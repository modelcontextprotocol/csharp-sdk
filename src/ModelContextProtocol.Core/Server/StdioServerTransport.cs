using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;

namespace ModelContextProtocol.Server;

/// <summary>
/// Provides an <see cref="ITransport"/> implemented via "stdio" (standard input/output).
/// </summary>
public sealed class StdioServerTransport : StreamServerTransport
{
    /// <summary>
    /// Initializes a new instance of the <see cref="StdioServerTransport"/> class.
    /// </summary>
    /// <param name="serverOptions">The server options.</param>
    /// <param name="loggerFactory">Optional logger factory used for logging employed by the transport.</param>
    /// <exception cref="ArgumentNullException"><paramref name="serverOptions"/> is <see langword="null"/> or contains a null name.</exception>
    public StdioServerTransport(McpServerOptions serverOptions, ILoggerFactory? loggerFactory = null)
        : this(GetServerName(serverOptions), loggerFactory: loggerFactory)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="StdioServerTransport"/> class.
    /// </summary>
    /// <param name="serverName">The name of the server.</param>
    /// <param name="loggerFactory">Optional logger factory used for logging employed by the transport.</param>
    /// <exception cref="ArgumentNullException"><paramref name="serverName"/> is <see langword="null"/>.</exception>
    public StdioServerTransport(string serverName, ILoggerFactory? loggerFactory = null)
        : base(new CancellableStdinStream(Console.OpenStandardInput()),
               new BufferedStream(Console.OpenStandardOutput()),
               serverName ?? throw new ArgumentNullException(nameof(serverName)),
               loggerFactory)
    {
    }

    private static string GetServerName(McpServerOptions serverOptions)
    {
        Throw.IfNull(serverOptions);

        return serverOptions.ServerInfo?.Name ?? McpServerImpl.DefaultImplementation.Name;
    }

    // Neither WindowsConsoleStream nor UnixConsoleStream respect CancellationTokens or cancel any I/O on Dispose.
    // WindowsConsoleStream will return an EOS on Ctrl-C, but that is not the only reason the shutdownToken may fire.
    private sealed class CancellableStdinStream(Stream stdinStream) : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            => stdinStream.ReadAsync(buffer, offset, count, cancellationToken).WaitAsync(cancellationToken);

#if NET
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            ValueTask<int> vt = stdinStream.ReadAsync(buffer, cancellationToken);
            return vt.IsCompletedSuccessfully ? vt : new(vt.AsTask().WaitAsync(cancellationToken));
        }
#endif

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                stdinStream.Dispose();
            }

            base.Dispose(disposing);
        }

        // The McpServer shouldn't call flush on the stdin Stream, but it doesn't need to throw just in case.
        public override void Flush() { }

        public override long Length => throw new NotSupportedException();

        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
