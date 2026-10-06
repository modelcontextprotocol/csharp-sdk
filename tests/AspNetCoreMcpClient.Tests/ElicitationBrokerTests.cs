using AspNetCoreMcpClient;
using ModelContextProtocol.Protocol;

namespace AspNetCoreMcpClient.Tests;

public class ElicitationBrokerTests
{
    [Fact]
    public async Task Response_CompletesMatchingPendingRequest()
    {
        var broker = new ElicitationBroker();
        var requestTask = broker.RequestAsync("alice", new() { Message = "Choose" }, TestContext.Current.CancellationToken).AsTask();
        var pending = broker.GetPending("alice");

        Assert.NotNull(pending);
        var expected = new ElicitResult { Action = "accept" };
        Assert.True(broker.TryRespond("alice", pending.Id, expected));

        Assert.Same(expected, await requestTask);
        Assert.Null(broker.GetPending("alice"));
    }

    [Fact]
    public async Task Cancellation_RemovesPendingRequest()
    {
        var broker = new ElicitationBroker();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var requestTask = broker.RequestAsync("alice", new() { Message = "Choose" }, cts.Token).AsTask();

        Assert.NotNull(broker.GetPending("alice"));
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => requestTask);
        Assert.Null(broker.GetPending("alice"));
    }

    [Fact]
    public async Task CancelAll_ReleasesEveryPendingRequest()
    {
        var broker = new ElicitationBroker();
        var alice = broker.RequestAsync("alice", new() { Message = "Choose" }, TestContext.Current.CancellationToken).AsTask();
        var bob = broker.RequestAsync("bob", new() { Message = "Choose" }, TestContext.Current.CancellationToken).AsTask();

        Assert.Equal(2, broker.CancelAll());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => alice);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => bob);
        Assert.Null(broker.GetPending("alice"));
        Assert.Null(broker.GetPending("bob"));
        Assert.Equal(0, broker.CancelAll());
    }
}
