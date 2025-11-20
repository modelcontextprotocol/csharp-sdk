/// <summary>
/// Test collection for tests that require exclusive execution (no parallel execution with any other tests).
/// This is needed for tests that use OpenTelemetry TracerProvider with the global static ActivitySource.
/// Without this, the TracerProvider would collect activities from ALL tests running in parallel.
/// </summary>
/// <remarks>
/// The fixture ensures mutual exclusion across ALL tests by using a static semaphore.
/// Tests in this collection will acquire the semaphore before running and release it after,
/// preventing any other test from running concurrently.
/// </remarks>
[CollectionDefinition(nameof(ExclusiveExecution))]
public sealed class ExclusiveExecution : ICollectionFixture<ExclusiveExecutionFixture>;

/// <summary>
/// Fixture that ensures only one test runs at a time across the entire test assembly.
/// </summary>
public sealed class ExclusiveExecutionFixture : IAsyncLifetime
{
    private static readonly SemaphoreSlim s_exclusiveLock = new(1, 1);

    public async ValueTask InitializeAsync()
    {
        // Acquire the lock before any test in this collection starts
        await s_exclusiveLock.WaitAsync();
    }

    public ValueTask DisposeAsync()
    {
        // Release the lock after the test completes
        s_exclusiveLock.Release();
        return default;
    }
}