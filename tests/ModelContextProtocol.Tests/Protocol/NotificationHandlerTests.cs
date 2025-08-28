using ModelContextProtocol.Client;

namespace ModelContextProtocol.Tests;

public class NotificationHandlerTests : ClientServerTestBase
{
    public NotificationHandlerTests(ITestOutputHelper testOutputHelper)
        : base(testOutputHelper)
    {
    }

    [Fact]
    public async Task RegistrationsAreRemovedWhenDisposed()
    {
        const string NotificationName = "somethingsomething";
        await using McpClient client = await CreateMcpClientForServer();

        const int Iterations = 10;

        int counter = 0;
        for (int i = 0; i < Iterations; i++)
        {
            var tcs = new TaskCompletionSource<bool>();
            await using (client.RegisterNotificationHandler(NotificationName, (notification, cancellationToken) =>
                {
                    Interlocked.Increment(ref counter);
                    tcs.SetResult(true);
                    return default;
                }))
            {
                await Server.SendNotificationAsync(NotificationName, TestContext.Current.CancellationToken);
                await tcs.Task;
            }
        }

        Assert.Equal(Iterations, counter);
    }

    [Fact]
    public async Task MultipleRegistrationsResultInMultipleCallbacks()
    {
        const string NotificationName = "somethingsomething";
        await using McpClient client = await CreateMcpClientForServer();

        const int RegistrationCount = 10;

        int remaining = RegistrationCount;
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        IAsyncDisposable[] registrations = new IAsyncDisposable[RegistrationCount];
        for (int i = 0; i < registrations.Length; i++)
        {
            registrations[i] = client.RegisterNotificationHandler(NotificationName, (notification, cancellationToken) =>
            {
                int result = Interlocked.Decrement(ref remaining);
                Assert.InRange(result, 0, RegistrationCount);
                if (result == 0)
                {
                    tcs.TrySetResult(true);
                }

                return default;
            });
        }

        try
        {
            await Server.SendNotificationAsync(NotificationName, TestContext.Current.CancellationToken);
            await tcs.Task;
        }
        finally
        {
            for (int i = registrations.Length - 1; i >= 0; i--)
            {
                await registrations[i].DisposeAsync();
            }
        }
    }

    [Fact]
    public async Task MultipleHandlersRunEvenIfOneThrows()
    {
        const string NotificationName = "somethingsomething";
        await using McpClient client = await CreateMcpClientForServer();

        const int RegistrationCount = 10;

        int remaining = RegistrationCount;
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        IAsyncDisposable[] registrations = new IAsyncDisposable[RegistrationCount];
        for (int i = 0; i < registrations.Length; i++)
        {
            registrations[i] = client.RegisterNotificationHandler(NotificationName, (notification, cancellationToken) =>
            {
                int result = Interlocked.Decrement(ref remaining);
                Assert.InRange(result, 0, RegistrationCount);
                if (result == 0)
                {
                    tcs.TrySetResult(true);
                }

                throw new InvalidOperationException("Test exception");
            });
        }

        try
        {
            await Server.SendNotificationAsync(NotificationName, TestContext.Current.CancellationToken);
            await tcs.Task;
        }
        finally
        {
            for (int i = registrations.Length - 1; i >= 0; i--)
            {
                await registrations[i].DisposeAsync();
            }
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public async Task DisposeAsyncDoesNotCompleteWhileNotificationHandlerRuns(int numberOfDisposals)
    {
        const string NotificationName = "somethingsomething";
        await using McpClient client = await CreateMcpClientForServer();

        var handlerRunning = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseHandler = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        IAsyncDisposable registration = client.RegisterNotificationHandler(NotificationName, async (notification, cancellationToken) =>
        {
            handlerRunning.SetResult(true);
            await releaseHandler.Task;
        });

        await Server.SendNotificationAsync(NotificationName, TestContext.Current.CancellationToken);
        await handlerRunning.Task;

        var disposals = new ValueTask[numberOfDisposals];
        for (int i = 0; i < numberOfDisposals; i++)
        {
            disposals[i] = registration.DisposeAsync();
        }

        await Task.Delay(1, TestContext.Current.CancellationToken);
        
        foreach (ValueTask disposal in disposals)
        {
            Assert.False(disposal.IsCompleted);
        }

        releaseHandler.SetResult(true);

        foreach (ValueTask disposal in disposals)
        {
            await disposal;
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public async Task DisposeAsyncCompletesImmediatelyWhenInvokedFromHandler(int numberOfDisposals)
    {
        const string NotificationName = "somethingsomething";
        await using McpClient client = await CreateMcpClientForServer();

        var handlerRunning = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseHandler = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        IAsyncDisposable? registration = null;
        await using var _ = registration = client.RegisterNotificationHandler(NotificationName, async (notification, cancellationToken) =>
        {
            for (int i = 0; i < numberOfDisposals; i++)
            {
                Assert.NotNull(registration);
                ValueTask disposal = registration!.DisposeAsync();
                Assert.True(disposal.IsCompletedSuccessfully);
                await disposal;
            }

            handlerRunning.SetResult(true);
        });

        await Server.SendNotificationAsync(NotificationName, TestContext.Current.CancellationToken);
        await handlerRunning.Task;
    }
}
