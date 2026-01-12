using Microsoft.Extensions.Time.Testing;

namespace ModelContextProtocol.AspNetCore.Tests;

public class InMemorySessionStoreTests
{
    private readonly FakeTimeProvider _timeProvider = new();
    private readonly InMemorySessionStore _store;

    public InMemorySessionStoreTests()
    {
        _timeProvider.SetUtcNow(new DateTimeOffset(2025, 1, 1, 12, 0, 0, TimeSpan.Zero));
        _store = new InMemorySessionStore(_timeProvider);
    }

    [Fact]
    public async Task SaveAsync_StoresSession()
    {
        var metadata = CreateTestMetadata("session-1");

        await _store.SaveAsync(metadata, TestContext.Current.CancellationToken);

        Assert.Equal(1, _store.Count);
    }

    [Fact]
    public async Task SaveAsync_OverwritesExistingSession()
    {
        var metadata1 = CreateTestMetadata("session-1", userValue: "user-1");
        var metadata2 = CreateTestMetadata("session-1", userValue: "user-2");

        await _store.SaveAsync(metadata1, TestContext.Current.CancellationToken);
        await _store.SaveAsync(metadata2, TestContext.Current.CancellationToken);

        var retrieved = await _store.GetAsync("session-1", TestContext.Current.CancellationToken);
        Assert.Equal(1, _store.Count);
        Assert.Equal("user-2", retrieved?.UserIdClaimValue);
    }

    [Fact]
    public async Task GetAsync_ReturnsStoredSession()
    {
        var metadata = CreateTestMetadata("session-1", userValue: "test-user");
        await _store.SaveAsync(metadata, TestContext.Current.CancellationToken);

        var retrieved = await _store.GetAsync("session-1", TestContext.Current.CancellationToken);

        Assert.NotNull(retrieved);
        Assert.Equal("session-1", retrieved.SessionId);
        Assert.Equal("test-user", retrieved.UserIdClaimValue);
    }

    [Fact]
    public async Task GetAsync_ReturnsNullForNonExistent()
    {
        var result = await _store.GetAsync("non-existent", TestContext.Current.CancellationToken);

        Assert.Null(result);
    }

    [Fact]
    public async Task UpdateActivityAsync_UpdatesTimestamp()
    {
        var metadata = CreateTestMetadata("session-1");
        await _store.SaveAsync(metadata, TestContext.Current.CancellationToken);

        var newActivity = new DateTime(2025, 1, 1, 13, 0, 0, DateTimeKind.Utc);
        await _store.UpdateActivityAsync("session-1", newActivity, TestContext.Current.CancellationToken);

        var retrieved = await _store.GetAsync("session-1", TestContext.Current.CancellationToken);
        Assert.Equal(newActivity, retrieved?.LastActivityUtc);
    }

    [Fact]
    public async Task UpdateActivityAsync_DoesNothingForNonExistent()
    {
        // Should not throw
        await _store.UpdateActivityAsync("non-existent", DateTime.UtcNow, TestContext.Current.CancellationToken);
        Assert.Equal(0, _store.Count);
    }

    [Fact]
    public async Task RemoveAsync_RemovesSession()
    {
        var metadata = CreateTestMetadata("session-1");
        await _store.SaveAsync(metadata, TestContext.Current.CancellationToken);

        var removed = await _store.RemoveAsync("session-1", TestContext.Current.CancellationToken);

        Assert.True(removed);
        Assert.Equal(0, _store.Count);
    }

    [Fact]
    public async Task RemoveAsync_ReturnsFalseForNonExistent()
    {
        var removed = await _store.RemoveAsync("non-existent", TestContext.Current.CancellationToken);

        Assert.False(removed);
    }

    [Fact]
    public async Task PruneIdleSessionsAsync_RemovesIdleSessions()
    {
        // Create sessions at different times
        _timeProvider.SetUtcNow(new DateTimeOffset(2025, 1, 1, 10, 0, 0, TimeSpan.Zero));
        var oldSession = CreateTestMetadata("old-session");
        oldSession.LastActivityUtc = _timeProvider.GetUtcNow().DateTime;
        await _store.SaveAsync(oldSession, TestContext.Current.CancellationToken);

        _timeProvider.SetUtcNow(new DateTimeOffset(2025, 1, 1, 12, 0, 0, TimeSpan.Zero));
        var newSession = CreateTestMetadata("new-session");
        newSession.LastActivityUtc = _timeProvider.GetUtcNow().DateTime;
        await _store.SaveAsync(newSession, TestContext.Current.CancellationToken);

        // Advance time and prune with 1 hour timeout
        _timeProvider.SetUtcNow(new DateTimeOffset(2025, 1, 1, 12, 30, 0, TimeSpan.Zero));
        var removed = await _store.PruneIdleSessionsAsync(TimeSpan.FromHours(1), TestContext.Current.CancellationToken);

        Assert.Equal(1, removed);
        Assert.Equal(1, _store.Count);
        Assert.Null(await _store.GetAsync("old-session", TestContext.Current.CancellationToken));
        Assert.NotNull(await _store.GetAsync("new-session", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task PruneIdleSessionsAsync_ReturnsZeroWhenNoIdleSessions()
    {
        var metadata = CreateTestMetadata("session-1");
        metadata.LastActivityUtc = _timeProvider.GetUtcNow().DateTime;
        await _store.SaveAsync(metadata, TestContext.Current.CancellationToken);

        var removed = await _store.PruneIdleSessionsAsync(TimeSpan.FromHours(1), TestContext.Current.CancellationToken);

        Assert.Equal(0, removed);
        Assert.Equal(1, _store.Count);
    }

    [Fact]
    public async Task Clear_RemovesAllSessions()
    {
        // Add multiple sessions
        await _store.SaveAsync(CreateTestMetadata("session-1"), TestContext.Current.CancellationToken);
        await _store.SaveAsync(CreateTestMetadata("session-2"), TestContext.Current.CancellationToken);
        await _store.SaveAsync(CreateTestMetadata("session-3"), TestContext.Current.CancellationToken);

        Assert.Equal(3, _store.Count);

        _store.Clear();

        Assert.Equal(0, _store.Count);
    }

    [Fact]
    public async Task MultipleSessions_WorkCorrectly()
    {
        await _store.SaveAsync(CreateTestMetadata("session-1", userValue: "user-1"), TestContext.Current.CancellationToken);
        await _store.SaveAsync(CreateTestMetadata("session-2", userValue: "user-2"), TestContext.Current.CancellationToken);
        await _store.SaveAsync(CreateTestMetadata("session-3", userValue: "user-3"), TestContext.Current.CancellationToken);

        Assert.Equal(3, _store.Count);

        var session1 = await _store.GetAsync("session-1", TestContext.Current.CancellationToken);
        var session2 = await _store.GetAsync("session-2", TestContext.Current.CancellationToken);
        var session3 = await _store.GetAsync("session-3", TestContext.Current.CancellationToken);

        Assert.Equal("user-1", session1?.UserIdClaimValue);
        Assert.Equal("user-2", session2?.UserIdClaimValue);
        Assert.Equal("user-3", session3?.UserIdClaimValue);
    }

    [Fact]
    public async Task ConcurrentAccess_IsThreadSafe()
    {
        var tasks = new List<Task>();
        var ct = TestContext.Current.CancellationToken;

        // Add 100 sessions concurrently
        for (int i = 0; i < 100; i++)
        {
            var sessionId = $"session-{i}";
            tasks.Add(_store.SaveAsync(CreateTestMetadata(sessionId), ct));
        }

        await Task.WhenAll(tasks);

        Assert.Equal(100, _store.Count);

        // Read all sessions concurrently
        var readTasks = Enumerable.Range(0, 100)
            .Select(i => _store.GetAsync($"session-{i}", ct))
            .ToList();

        var results = await Task.WhenAll(readTasks);

        Assert.All(results, r => Assert.NotNull(r));
    }

    private SessionMetadata CreateTestMetadata(
        string sessionId,
        string? userType = "sub",
        string? userValue = null,
        string? userIssuer = "test-issuer")
    {
        return new SessionMetadata
        {
            SessionId = sessionId,
            UserIdClaimType = userType,
            UserIdClaimValue = userValue ?? $"user-for-{sessionId}",
            UserIdClaimIssuer = userIssuer,
            CreatedAtUtc = _timeProvider.GetUtcNow().DateTime,
            LastActivityUtc = _timeProvider.GetUtcNow().DateTime
        };
    }
}
