using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using ModelContextProtocol.Client;

namespace ResumabilityDemo.Client;

/// <summary>
/// Represents a pending background operation.
/// </summary>
public sealed class PendingOperation
{
    public required int Id { get; init; }
    public required string Description { get; init; }
    public required Task Task { get; init; }
    public required CancellationTokenSource Cts { get; init; }

    public string Status => Task.IsCompleted ? "Completed" :
                            Task.IsFaulted ? "Faulted" :
                            Task.IsCanceled ? "Canceled" : "Running";
}

/// <summary>
/// Manages shared client state including connection and pending operations.
/// </summary>
public sealed class ClientState : IAsyncDisposable
{
    private McpClient? _client;
    private HttpClientTransport? _transport;
    private readonly List<PendingOperation> _pendingOperations = [];
    private int _nextOperationId = 1;

    public ILoggerFactory? LoggerFactory { get; set; }
    public Uri ServerUri { get; set; } = new("http://localhost:5000/mcp");

    public McpClient? Client => _client;
    public bool IsConnected => _client is not null;
    public IReadOnlyList<PendingOperation> PendingOperations => _pendingOperations;
    public int PendingCount => _pendingOperations.Count;

    public async Task ConnectAsync()
    {
        if (_client is not null)
        {
            throw new InvalidOperationException("Already connected. Use 'disconnect' first.");
        }

        _transport = new HttpClientTransport(new HttpClientTransportOptions
        {
            Endpoint = ServerUri,
            TransportMode = HttpTransportMode.StreamableHttp,
        }, loggerFactory: LoggerFactory);

        _client = await McpClient.CreateAsync(_transport, loggerFactory: LoggerFactory);
    }

    public async Task DisconnectAsync(bool graceful)
    {
        if (_client is null && _transport is null)
        {
            throw new InvalidOperationException("Not connected.");
        }

        if (graceful)
        {
            if (_client is not null)
            {
                await _client.DisposeAsync();
            }
        }
        else
        {
            // Dispose transport directly without graceful shutdown
            // This simulates an abrupt network disconnection
            if (_transport is not null)
            {
                await _transport.DisposeAsync();
            }
        }

        _client = null;
        _transport = null;
    }

    public void EnsureConnected()
    {
        if (_client is null)
        {
            throw new InvalidOperationException("Not connected. Use 'connect' first.");
        }
    }

    public int AddPendingOperation(string description, Task task, CancellationTokenSource cts)
    {
        var id = _nextOperationId++;
        _pendingOperations.Add(new PendingOperation
        {
            Id = id,
            Description = description,
            Task = task,
            Cts = cts
        });
        return id;
    }

    public bool TryCancelOperation(int operationId)
    {
        var op = _pendingOperations.FirstOrDefault(o => o.Id == operationId);
        if (op is null)
        {
            return false;
        }

        op.Cts.Cancel();
        return true;
    }

    public void RemoveOperation(int operationId)
    {
        var op = _pendingOperations.FirstOrDefault(o => o.Id == operationId);
        if (op is not null)
        {
            _pendingOperations.Remove(op);
            op.Cts.Dispose();
        }
    }

    public void CleanupCompletedOperations()
    {
        var completed = _pendingOperations.Where(o => o.Task.IsCompleted).ToList();
        foreach (var op in completed)
        {
            _pendingOperations.Remove(op);
            op.Cts.Dispose();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_client is not null)
        {
            await _client.DisposeAsync();
        }
        LoggerFactory?.Dispose();
    }
}
