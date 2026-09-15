using AspNetCoreMcpClient;
using Microsoft.Extensions.Time.Testing;

namespace AspNetCoreMcpClient.Tests;

public class SessionClientRegistryTests
{
    [Fact]
    public async Task ConcurrentOperationsForOneSession_CreateOneClientAndDoNotOverlap()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var factoryCalls = 0;
        var concurrentOperations = 0;
        var maximumConcurrency = 0;
        var releaseFirstOperation = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstOperationStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var registry = new SessionClientRegistry<FakeClient>(
            (_, _) => Task.FromResult(new FakeClient(Interlocked.Increment(ref factoryCalls))),
            TimeProvider.System,
            TimeSpan.FromMinutes(5));

        async ValueTask<int> Operation(FakeClient client, CancellationToken _)
        {
            var current = Interlocked.Increment(ref concurrentOperations);
            maximumConcurrency = Math.Max(maximumConcurrency, current);
            firstOperationStarted.TrySetResult();
            await releaseFirstOperation.Task.WaitAsync(cancellationToken);
            Interlocked.Decrement(ref concurrentOperations);
            return client.Id;
        }

        var first = registry.ExecuteAsync("alice", Operation, cancellationToken);
        await firstOperationStarted.Task.WaitAsync(cancellationToken);
        var second = registry.ExecuteAsync("alice", Operation, cancellationToken);
        releaseFirstOperation.SetResult();

        var results = await Task.WhenAll(first, second);

        Assert.Equal([1, 1], results);
        Assert.Equal(1, factoryCalls);
        Assert.Equal(1, maximumConcurrency);
    }

    [Fact]
    public async Task DifferentSessions_CanRunConcurrently()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var concurrentOperations = 0;
        var bothStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var registry = new SessionClientRegistry<FakeClient>(
            (_, _) => Task.FromResult(new FakeClient(1)),
            TimeProvider.System,
            TimeSpan.FromMinutes(5));

        async ValueTask<int> Operation(FakeClient _, CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref concurrentOperations) == 2)
            {
                bothStarted.SetResult();
            }

            await bothStarted.Task.WaitAsync(cancellationToken);
            Interlocked.Decrement(ref concurrentOperations);
            return 1;
        }

        await Task.WhenAll(
            registry.ExecuteAsync("alice", Operation, cancellationToken),
            registry.ExecuteAsync("bob", Operation, cancellationToken));
    }

    [Fact]
    public async Task RemoveAndIdleCleanup_DisposeClients()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var timeProvider = new FakeTimeProvider();
        var clients = new List<FakeClient>();
        await using var registry = new SessionClientRegistry<FakeClient>(
            (_, _) =>
            {
                var client = new FakeClient(clients.Count + 1);
                clients.Add(client);
                return Task.FromResult(client);
            },
            timeProvider,
            TimeSpan.FromMinutes(5));

        await registry.ExecuteAsync("explicit", static (client, _) => ValueTask.FromResult(client.Id), cancellationToken);
        await registry.ExecuteAsync("idle", static (client, _) => ValueTask.FromResult(client.Id), cancellationToken);

        Assert.True(await registry.RemoveAsync("explicit"));
        timeProvider.Advance(TimeSpan.FromMinutes(6));
        Assert.Equal(1, await registry.RemoveIdleAsync());
        Assert.All(clients, client => Assert.True(client.IsDisposed));
    }

    private sealed class FakeClient(int id) : IAsyncDisposable
    {
        public int Id { get; } = id;

        public bool IsDisposed { get; private set; }

        public ValueTask DisposeAsync()
        {
            IsDisposed = true;
            return default;
        }
    }
}
