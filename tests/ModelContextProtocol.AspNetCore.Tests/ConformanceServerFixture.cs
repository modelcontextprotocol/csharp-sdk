using Microsoft.AspNetCore.Builder;
using ModelContextProtocol.Tests.Utils;

namespace ModelContextProtocol.ConformanceTests;

/// <summary>
/// Shared fixture that starts a single ConformanceServer exposing both the legacy stateful MCP
/// lifecycle (at <see cref="ServerUrl"/>, "/") and the SEP-2575 stateless lifecycle (at
/// <see cref="StatelessServerUrl"/>, "/stateless") on one port. A single long-lived server avoids
/// the TCP TIME_WAIT conflicts that per-test restarts on a fixed port cause on Windows. Kestrel
/// assigns a loopback port, and initialization awaits this host's startup before publishing its URL.
/// Shared by <see cref="ServerConformanceTests"/> and <see cref="CachingConformanceTests"/>
/// via <see cref="ConformanceServerCollection"/>.
/// </summary>
public sealed class ConformanceServerFixture : IAsyncLifetime
{
    private readonly Func<WebApplication> _createApplication;
    private readonly TimeProvider _timeProvider;
    private WebApplication? _app;
    private string? _serverUrl;
    private CancellationTokenSource? _startupTimeoutCts;
    private CancellationTokenSource? _startupCts;
    private Task? _startupTask;
    private Task? _stopTask;
    private Task? _disposeTask;
    private bool _startupOutcomeObserved;

    public ConformanceServerFixture()
        : this(() => new ConformanceServer.Program().CreateApplication(["--urls", "http://127.0.0.1:0"]))
    {
    }

    internal ConformanceServerFixture(Func<WebApplication> createApplication)
        : this(createApplication, TimeProvider.System)
    {
    }

    internal ConformanceServerFixture(Func<WebApplication> createApplication, TimeProvider timeProvider)
    {
        _createApplication = createApplication;
        _timeProvider = timeProvider;
    }

    /// <summary>Base URL of the stateful MCP endpoint (mapped at "/").</summary>
    public string ServerUrl => _serverUrl ?? throw new InvalidOperationException("The conformance server has not started.");

    /// <summary>
    /// URL of the stateless MCP endpoint (mapped at "/stateless"), used by the 2026-07-28
    /// scenarios (caching, MRTR, SEP-2243) that negotiate the stateless lifecycle.
    /// </summary>
    public string StatelessServerUrl => $"{ServerUrl}/stateless";

    public ValueTask InitializeAsync() => InitializeAsync(TestContext.Current.CancellationToken);

    internal async ValueTask InitializeAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _startupTimeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(30), _timeProvider);
        _startupCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _startupTimeoutCts.Token);
        _app = _createApplication();
        _startupTask = _app.StartAsync(_startupCts.Token);
        var cancelled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = _startupCts.Token.Register(() => cancelled.TrySetResult(true));

        try
        {
            await Task.WhenAny(_startupTask, cancelled.Task);
            if (_startupTask.IsCompleted)
            {
                _startupOutcomeObserved = true;
                await _startupTask;
            }
            _startupCts.Token.ThrowIfCancellationRequested();
            _startupTimeoutCts.CancelAfter(Timeout.InfiniteTimeSpan);
        }
        catch (OperationCanceledException error) when (!cancellationToken.IsCancellationRequested &&
            _startupCts.IsCancellationRequested)
        {
            throw new TimeoutException("ConformanceServer failed to start within 30 seconds.", error);
        }
        catch (OperationCanceledException error) when (!cancellationToken.IsCancellationRequested &&
            _app.Lifetime.ApplicationStopping.IsCancellationRequested)
        {
            throw new InvalidOperationException("ConformanceServer stopped before startup completed.", error);
        }

        if (!_app.Lifetime.ApplicationStarted.IsCancellationRequested ||
            _app.Lifetime.ApplicationStopping.IsCancellationRequested)
        {
            throw new InvalidOperationException("ConformanceServer stopped before startup completed.");
        }

        _serverUrl = _app.Urls.Single();
    }

    public async ValueTask DisposeAsync()
    {
        if (_app is null)
        {
            DisposeStartupCancellation();
            return;
        }

        using var shutdownCts = new CancellationTokenSource(TestConstants.DefaultTimeout, _timeProvider);
        try
        {
            if (_startupCts is not null)
            {
                await _startupCts.CancelAsync().WaitAsync(shutdownCts.Token);
            }

            if (_startupTask is not null)
            {
                // Wait for completion without throwing its already-reported startup error.
                // A timed-out, still-running startup retains ownership of the app and tokens.
                await WaitForCompletionAsync(_startupTask, shutdownCts.Token);
            }

            shutdownCts.Token.ThrowIfCancellationRequested();
            _stopTask ??= _app.StopAsync(shutdownCts.Token);
            await WaitForCompletionAsync(_stopTask, shutdownCts.Token);
            shutdownCts.Token.ThrowIfCancellationRequested();
            _disposeTask ??= _app.DisposeAsync().AsTask();
            await _disposeTask.WaitAsync(shutdownCts.Token);

            var startupOutcome = Task.CompletedTask;
            if (!_startupOutcomeObserved &&
                _startupTask is not null)
            {
                _startupOutcomeObserved = true;
                bool expectedCancellation = _startupTask.IsCanceled &&
                    (_startupCts?.IsCancellationRequested).GetValueOrDefault();
                if (!expectedCancellation)
                {
                    startupOutcome = _startupTask;
                }
            }

            _app = null;
            _serverUrl = null;
            DisposeStartupCancellation();

            // Observe failures only after safely releasing the host; neither a late startup
            // fault nor a StopAsync failure is converted into successful cleanup.
            await Task.WhenAll(_stopTask, startupOutcome);
        }
        catch (OperationCanceledException error) when (shutdownCts.IsCancellationRequested)
        {
            throw new TimeoutException("ConformanceServer cleanup did not complete within 60 seconds.", error);
        }
    }

    private async Task WaitForCompletionAsync(Task task, CancellationToken cancellationToken)
    {
        await Task.WhenAny(task).WaitAsync(cancellationToken);
    }

    private void DisposeStartupCancellation()
    {
        _startupCts?.Dispose();
        _startupTimeoutCts?.Dispose();
    }
}

/// <summary>
/// xUnit collection that shares one <see cref="ConformanceServerFixture"/> across the conformance
/// test classes so they run against a single server instance (and a single bound port).
/// </summary>
[CollectionDefinition(nameof(ConformanceServerCollection))]
public sealed class ConformanceServerCollection : ICollectionFixture<ConformanceServerFixture>;
