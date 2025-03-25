using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Protocol.Messages;
using ModelContextProtocol.Protocol.Transport;
using ModelContextProtocol.Utils.Json;
using System.Text.Json;

namespace ModelContextProtocol.Tests.Transport;

internal sealed class StreamClientTransport : TransportBase, IClientTransport
{
    private readonly JsonSerializerOptions _jsonOptions = McpJsonUtilities.DefaultOptions;
    private Task? _readTask;
    private CancellationTokenSource _shutdownCts = new CancellationTokenSource();
    private readonly TextReader _stdin;
    private readonly TextWriter _stdout;

    public StreamClientTransport(TextReader stdin, TextWriter stdout)
        : base(NullLoggerFactory.Instance)
    {
        _stdin = stdin;
        _stdout = stdout;
        _readTask = Task.Run(() => ReadMessagesAsync(_shutdownCts.Token), CancellationToken.None);
        SetConnected(true);
    }

    public Task ConnectAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public override async Task SendMessageAsync(IJsonRpcMessage message, CancellationToken cancellationToken = default)
    {
        string id = message is IJsonRpcMessageWithId messageWithId ?
            messageWithId.Id.ToString() :
            "(no id)";
     
        await _stdout.WriteLineAsync(JsonSerializer.Serialize(message)).ConfigureAwait(false);
        await _stdout.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task ReadMessagesAsync(CancellationToken cancellationToken)
    {
        while (await _stdin.ReadLineAsync(cancellationToken).ConfigureAwait(false) is string line)
        {
            if (!string.IsNullOrWhiteSpace(line))
            {
                try
                {
                    if (JsonSerializer.Deserialize<IJsonRpcMessage>(line.Trim(), _jsonOptions) is { } message)
                    {
                        await WriteMessageAsync(message, cancellationToken).ConfigureAwait(false);
                    }
                }
                catch (JsonException)
                {
                }
            }
        }
    }

    public override async ValueTask DisposeAsync()
    {
        if (_shutdownCts is { } shutdownCts)
        {
            await shutdownCts.CancelAsync().ConfigureAwait(false);
            shutdownCts.Dispose();
        }

        if (_readTask is Task readTask)
        {
            await readTask.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        }

        SetConnected(false);
    }
}
