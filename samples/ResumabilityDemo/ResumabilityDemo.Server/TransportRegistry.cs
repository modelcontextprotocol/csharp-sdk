using ModelContextProtocol.Protocol;
using System.Collections.Concurrent;

namespace ResumabilityDemo.Server;

/// <summary>
/// Tracks active POST transports for tool calls so they can be forcefully terminated
/// to simulate network disconnections.
/// </summary>
public sealed class TransportRegistry
{
    private readonly ConcurrentDictionary<string, ITransport> _transports = new();
    private readonly ILogger<TransportRegistry> _logger;

    public TransportRegistry(ILogger<TransportRegistry> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Gets the current count of registered transports.
    /// </summary>
    public int Count => _transports.Count;

    /// <summary>
    /// Registers a transport for tracking. Returns a disposable that removes the transport when disposed.
    /// </summary>
    public IDisposable Register(string id, ITransport transport)
    {
        _transports[id] = transport;
        _logger.LogInformation("Transport {Id} registered. Total: {Count}", id, _transports.Count);
        return new RegistrationHandle(this, id);
    }

    /// <summary>
    /// Removes a transport from tracking.
    /// </summary>
    public void Unregister(string id)
    {
        if (_transports.TryRemove(id, out _))
        {
            _logger.LogInformation("Transport {Id} unregistered. Total: {Count}", id, _transports.Count);
        }
    }

    /// <summary>
    /// Disposes all registered transports to simulate network disconnection.
    /// Returns the number of transports that were killed.
    /// </summary>
    public async Task<int> KillAllAsync()
    {
        var transports = _transports.ToArray();
        _transports.Clear();

        _logger.LogWarning("Killing {Count} transports!", transports.Length);

        foreach (var (id, transport) in transports)
        {
            try
            {
                _logger.LogInformation("Killing transport {Id}...", id);
                await transport.DisposeAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error killing transport {Id}", id);
            }
        }

        return transports.Length;
    }

    /// <summary>
    /// Gets info about all registered transports.
    /// </summary>
    public IReadOnlyList<string> GetRegisteredIds()
    {
        return _transports.Keys.ToList();
    }

    private sealed class RegistrationHandle : IDisposable
    {
        private readonly TransportRegistry _registry;
        private readonly string _id;
        private bool _disposed;

        public RegistrationHandle(TransportRegistry registry, string id)
        {
            _registry = registry;
            _id = id;
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                _registry.Unregister(_id);
            }
        }
    }
}
