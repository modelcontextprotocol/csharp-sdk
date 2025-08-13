using System.Collections.Concurrent;

namespace DynamicToolFiltering.Services;

/// <summary>
/// In-memory implementation of rate limiting service.
/// Note: This is for demonstration purposes. In production, use a distributed cache like Redis.
/// </summary>
public class InMemoryRateLimitingService : IRateLimitingService
{
    private readonly ConcurrentDictionary<string, List<UsageRecord>> _usageRecords = new();
    private readonly ILogger<InMemoryRateLimitingService> _logger;
    private readonly Timer _cleanupTimer;

    public InMemoryRateLimitingService(ILogger<InMemoryRateLimitingService> logger)
    {
        _logger = logger;
        
        // Run cleanup every 10 minutes
        _cleanupTimer = new Timer(async _ => await CleanupOldRecordsAsync(), null, TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(10));
    }

    public Task<int> GetUsageCountAsync(string userId, string toolName, DateTime windowStart, CancellationToken cancellationToken = default)
    {
        var key = GetKey(userId, toolName);
        
        if (!_usageRecords.TryGetValue(key, out var records))
        {
            return Task.FromResult(0);
        }

        lock (records)
        {
            var count = records.Count(r => r.Timestamp >= windowStart);
            return Task.FromResult(count);
        }
    }

    public Task RecordUsageAsync(string userId, string toolName, DateTime timestamp, CancellationToken cancellationToken = default)
    {
        var key = GetKey(userId, toolName);
        var record = new UsageRecord(timestamp);
        
        _usageRecords.AddOrUpdate(key, 
            new List<UsageRecord> { record },
            (_, existingRecords) =>
            {
                lock (existingRecords)
                {
                    existingRecords.Add(record);
                    return existingRecords;
                }
            });

        _logger.LogDebug("Recorded usage for {UserId}, {ToolName} at {Timestamp}", userId, toolName, timestamp);
        
        return Task.CompletedTask;
    }

    public Task CleanupOldRecordsAsync(CancellationToken cancellationToken = default)
    {
        var cutoffTime = DateTime.UtcNow.AddHours(-24); // Keep records for 24 hours
        var keysToRemove = new List<string>();
        
        foreach (var kvp in _usageRecords)
        {
            var records = kvp.Value;
            
            lock (records)
            {
                records.RemoveAll(r => r.Timestamp < cutoffTime);
                
                if (records.Count == 0)
                {
                    keysToRemove.Add(kvp.Key);
                }
            }
        }

        foreach (var key in keysToRemove)
        {
            _usageRecords.TryRemove(key, out _);
        }

        if (keysToRemove.Count > 0)
        {
            _logger.LogDebug("Cleaned up {Count} empty usage record collections", keysToRemove.Count);
        }

        return Task.CompletedTask;
    }

    public Task<Dictionary<string, int>> GetUsageStatisticsAsync(string userId, CancellationToken cancellationToken = default)
    {
        var statistics = new Dictionary<string, int>();
        var userPrefix = $"{userId}:";
        var windowStart = DateTime.UtcNow.AddHours(-1); // Last hour
        
        foreach (var kvp in _usageRecords)
        {
            if (kvp.Key.StartsWith(userPrefix))
            {
                var toolName = kvp.Key[userPrefix.Length..];
                var records = kvp.Value;
                
                lock (records)
                {
                    var count = records.Count(r => r.Timestamp >= windowStart);
                    if (count > 0)
                    {
                        statistics[toolName] = count;
                    }
                }
            }
        }

        return Task.FromResult(statistics);
    }

    private static string GetKey(string userId, string toolName) => $"{userId}:{toolName}";

    public void Dispose()
    {
        _cleanupTimer?.Dispose();
    }

    private record UsageRecord(DateTime Timestamp);
}