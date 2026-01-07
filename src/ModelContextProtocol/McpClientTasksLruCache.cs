using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace ModelContextProtocol.Client;

/// <summary>
/// A thread-safe Least Recently Used (LRU) cache for MCP client and tools.
/// </summary>
internal sealed class McpClientTasksLruCache : IDisposable
{
    private readonly Dictionary<string, (LinkedListNode<string> Node, Task<(McpClient Client, IList<McpClientTool> Tools)> Task)> _cache;
    private readonly LinkedList<string> _lruList;
    private readonly object _lock = new();
    private readonly int _capacity;

    public McpClientTasksLruCache(int capacity)
    {
        Debug.Assert(capacity > 0);
        _capacity = capacity;
        _cache = new Dictionary<string, (LinkedListNode<string>, Task<(McpClient, IList<McpClientTool>)>)>(capacity);
        _lruList = [];
    }

    public Task<(McpClient Client, IList<McpClientTool> Tools)> GetOrAdd<TState>(string key, Func<string, TState, Task<(McpClient, IList<McpClientTool>)>> valueFactory, TState state)
    {
        lock (_lock)
        {
            if (_cache.TryGetValue(key, out var existing))
            {
                _lruList.Remove(existing.Node);
                _lruList.AddLast(existing.Node);
                return existing.Task;
            }

            var value = valueFactory(key, state);
            var newNode = _lruList.AddLast(key);
            _cache[key] = (newNode, value);

            // Evict oldest if over capacity
            if (_cache.Count > _capacity)
            {
                string oldestKey = _lruList.First!.Value;
                _lruList.RemoveFirst();
                (_, Task<(McpClient Client, IList<McpClientTool> Tools)> task) = _cache[oldestKey];
                _cache.Remove(oldestKey);

                // Dispose evicted MCP client
                if (task.Status == TaskStatus.RanToCompletion)
                {
                    _ = task.Result.Client.DisposeAsync().AsTask();
                }
            }

            return value;
        }
    }

    public bool TryRemove(string key, [MaybeNullWhen(false)] out Task<(McpClient Client, IList<McpClientTool> Tools)>? task)
    {
        lock (_lock)
        {
            if (_cache.TryGetValue(key, out var entry))
            {
                _cache.Remove(key);
                _lruList.Remove(entry.Node);
                task = entry.Task;
                return true;
            }

            task = null;
            return false;
        }
    }
    
    public void Dispose()
    {
        lock (_lock)
        {
            foreach ((_, Task<(McpClient Client, IList<McpClientTool> Tools)> task) in _cache.Values)
            {
                if (task.Status == TaskStatus.RanToCompletion)
                {
                    _ = task.Result.Client.DisposeAsync().AsTask();
                }
            }
        }
    }
}
