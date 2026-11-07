using Microsoft.Extensions.Time.Testing;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Tests.Utils;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Channels;

namespace ModelContextProtocol.Tests.Client;

/// <summary>
/// Regression tests for the fallback from the 2026-07-28 protocol revision to an initialize-handshake protocol in
/// <see cref="McpClient"/>. With default options (<c>ProtocolVersion = null</c>) the client prefers
/// 2026-07-28 but probes with <c>server/discover</c>, falls back to the <c>initialize</c>
/// handshake when the server only supports that path, and accepts whatever supported protocol version the
/// server negotiates. Pinning <c>ProtocolVersion</c> to <c>2026-07-28</c> instead makes it the
/// minimum too, so the client refuses to fall back.
/// </summary>
/// <remarks>
/// The originally shipped initialize-handshake fallback logic compared the server's response
/// against the requested version and threw when an initialize-handshake server downgraded to (say)
/// <c>"2025-06-18"</c>, even though negotiation succeeded. These tests guard against that regression.
/// </remarks>
public class July2026ProtocolFallbackTests(ITestOutputHelper testOutputHelper) : LoggedTest(testOutputHelper)
{
    [Theory]
    [InlineData(McpProtocolVersions.November2025ProtocolVersion, McpProtocolVersions.November2025ProtocolVersion)]
    [InlineData(McpProtocolVersions.November2025ProtocolVersion, McpProtocolVersions.June2025ProtocolVersion)]
    [InlineData(McpProtocolVersions.June2025ProtocolVersion, McpProtocolVersions.March2025ProtocolVersion)]
    public async Task MaximumProtocolVersion_InitializeAcceptsOlderSupportedVersion(string maximum, string negotiated)
    {
        await using var transport = new InitializeHandshakeServerTestTransport(negotiated);
        await using var client = await McpClient.CreateAsync(transport,
            new McpClientOptions { MaximumProtocolVersion = maximum },
            LoggerFactory, TestContext.Current.CancellationToken);

        Assert.False(transport.ServerDiscoverProbed);
        Assert.Equal(maximum, transport.InitializeProtocolVersion);
        Assert.Equal(negotiated, client.NegotiatedProtocolVersion);
    }

    [Fact]
    public async Task MaximumProtocolVersion_ModernVersionUsesDiscover()
    {
        await using var transport = new PerRequestMetadataRetryTestTransport();
        await using var client = await McpClient.CreateAsync(transport,
            new McpClientOptions { MaximumProtocolVersion = McpProtocolVersions.July2026ProtocolVersion },
            LoggerFactory, TestContext.Current.CancellationToken);

        Assert.Equal(2, transport.ServerDiscoverRequests);
        Assert.False(transport.InitializeReceived);
        Assert.Equal(McpProtocolVersions.July2026ProtocolVersion, client.NegotiatedProtocolVersion);
    }

    [Fact]
    public async Task MaximumProtocolVersion_ModernCeilingAllowsInitializeFallback()
    {
        await using var transport = new InitializeHandshakeServerTestTransport(McpProtocolVersions.June2025ProtocolVersion);
        await using var client = await McpClient.CreateAsync(transport,
            new McpClientOptions { MaximumProtocolVersion = McpProtocolVersions.July2026ProtocolVersion },
            LoggerFactory, TestContext.Current.CancellationToken);

        Assert.True(transport.ServerDiscoverProbed);
        Assert.Equal(McpProtocolVersions.November2025ProtocolVersion, transport.InitializeProtocolVersion);
        Assert.Equal(McpProtocolVersions.June2025ProtocolVersion, client.NegotiatedProtocolVersion);
    }

    [Theory]
    [InlineData(McpProtocolVersions.November2025ProtocolVersion)]
    [InlineData("not-a-protocol-version")]
    public async Task MaximumProtocolVersion_RejectsNewerOrMalformedInitializeResponse(string serverVersion)
    {
        await using var transport = new InitializeHandshakeServerTestTransport(serverVersion);

        var exception = await Assert.ThrowsAsync<McpException>(async () =>
        {
            await using var client = await McpClient.CreateAsync(transport,
                new McpClientOptions { MaximumProtocolVersion = McpProtocolVersions.June2025ProtocolVersion },
                LoggerFactory, TestContext.Current.CancellationToken);
        });

        Assert.False(transport.ServerDiscoverProbed);
        Assert.Equal(McpProtocolVersions.June2025ProtocolVersion, transport.InitializeProtocolVersion);
        Assert.Contains(serverVersion, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MaximumProtocolVersion_RejectsDiscoverWithoutMutualVersion()
    {
        await using var transport = new PerRequestMetadataRetryTestTransport(["2027-01-01"]);

        var exception = await Assert.ThrowsAsync<McpException>(async () =>
        {
            await using var client = await McpClient.CreateAsync(transport,
                new McpClientOptions { MaximumProtocolVersion = McpProtocolVersions.July2026ProtocolVersion },
                LoggerFactory, TestContext.Current.CancellationToken);
        });

        Assert.Equal(1, transport.ServerDiscoverRequests);
        Assert.False(transport.InitializeReceived);
        Assert.Contains("No mutually supported", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MaximumProtocolVersion_RejectsConflictWithExactVersionBeforeConnecting()
    {
        await using var transport = new InitializeHandshakeServerTestTransport(McpProtocolVersions.November2025ProtocolVersion);

        await Assert.ThrowsAsync<ArgumentException>(() => McpClient.CreateAsync(transport,
            new McpClientOptions
            {
                ProtocolVersion = McpProtocolVersions.November2025ProtocolVersion,
                MaximumProtocolVersion = McpProtocolVersions.July2026ProtocolVersion,
            }, LoggerFactory, TestContext.Current.CancellationToken));

        Assert.False(transport.ServerDiscoverProbed);
        Assert.False(transport.InitializeReceived);
    }

    [Fact]
    public void MaximumProtocolVersion_RejectsUnsupportedVersion()
    {
        Assert.Throws<ArgumentException>(() => new McpClientOptions { MaximumProtocolVersion = "2026-01-01" });
    }

    [Fact]
    public async Task MaximumProtocolVersion_RejectsResumeAboveCeiling()
    {
        await using var transport = new InitializeHandshakeServerTestTransport(McpProtocolVersions.November2025ProtocolVersion);
        var resumeOptions = new ResumeClientSessionOptions
        {
            ServerCapabilities = new ServerCapabilities(),
            ServerInfo = new Implementation { Name = "test-server", Version = "1.0" },
            NegotiatedProtocolVersion = McpProtocolVersions.November2025ProtocolVersion,
        };

        await Assert.ThrowsAsync<ArgumentException>(() => McpClient.ResumeSessionAsync(transport,
            resumeOptions,
            new McpClientOptions { MaximumProtocolVersion = McpProtocolVersions.June2025ProtocolVersion },
            LoggerFactory, TestContext.Current.CancellationToken));

        Assert.False(transport.ServerDiscoverProbed);
        Assert.False(transport.InitializeReceived);
    }

    [Fact]
    public async Task Client_OnMethodNotFound_FallsBackTo_Initialize_AcceptsDowngradedVersion()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var transport = new InitializeHandshakeServerTestTransport(serverNegotiatedVersion: McpProtocolVersions.June2025ProtocolVersion);

        // Default options (ProtocolVersion = null) prefer 2026-07-28 but allow automatic fallback.
        await using var client = await McpClient.CreateAsync(transport, new McpClientOptions(),
            loggerFactory: LoggerFactory, cancellationToken: ct);

        Assert.True(transport.ServerDiscoverProbed);
        Assert.True(transport.InitializeReceived);
        Assert.Equal(McpProtocolVersions.November2025ProtocolVersion, transport.InitializeProtocolVersion);
        Assert.Equal(McpProtocolVersions.June2025ProtocolVersion, client.NegotiatedProtocolVersion);
    }

    [Fact]
    public async Task Client_OnInvalidParams_FallsBackTo_Initialize()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var transport = new InitializeHandshakeServerTestTransport(
            serverNegotiatedVersion: McpProtocolVersions.November2025ProtocolVersion,
            probeErrorCode: (int)McpErrorCode.InvalidParams);

        // Default options (ProtocolVersion = null) prefer 2026-07-28 but allow automatic fallback.
        await using var client = await McpClient.CreateAsync(transport, new McpClientOptions(),
            loggerFactory: LoggerFactory, cancellationToken: ct);

        Assert.True(transport.InitializeReceived);
        Assert.Equal(McpProtocolVersions.November2025ProtocolVersion, transport.InitializeProtocolVersion);
        Assert.Equal(McpProtocolVersions.November2025ProtocolVersion, client.NegotiatedProtocolVersion);
    }

    [Fact]
    public async Task Client_OnInitializeFallback_RejectsPerRequestMetadataInitializeResponse()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var transport = new InitializeHandshakeServerTestTransport(
            serverNegotiatedVersion: McpProtocolVersions.July2026ProtocolVersion);

        var exception = await Assert.ThrowsAnyAsync<Exception>(async () =>
        {
            await using var client = await McpClient.CreateAsync(transport, new McpClientOptions(),
                loggerFactory: LoggerFactory, cancellationToken: ct);
        });

        Assert.IsType<McpException>(exception);
        Assert.True(transport.InitializeReceived);
        Assert.Equal(McpProtocolVersions.November2025ProtocolVersion, transport.InitializeProtocolVersion);
        Assert.Contains("mismatch", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Client_WithPinnedJuly2026Version_RefusesFallback_ToInitializeHandshakeServer()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var transport = new InitializeHandshakeServerTestTransport(serverNegotiatedVersion: McpProtocolVersions.June2025ProtocolVersion);

        var exception = await Assert.ThrowsAnyAsync<Exception>(async () =>
        {
            await using var client = await McpClient.CreateAsync(transport, new McpClientOptions
            {
                // Pinning the version makes it the minimum too, so the client refuses to fall back.
                ProtocolVersion = McpProtocolVersions.July2026ProtocolVersion,
            }, loggerFactory: LoggerFactory, cancellationToken: ct);
        });

        Assert.IsType<McpException>(exception);
        Assert.Contains("2026-07-28", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task InitializeHandshakeClient_WithExplicitPin_StillRequires_ExactVersionMatch()
    {
        var ct = TestContext.Current.CancellationToken;
        // Server responds with a DIFFERENT version than the one the user pinned.
        await using var transport = new InitializeHandshakeServerTestTransport(serverNegotiatedVersion: McpProtocolVersions.March2025ProtocolVersion);

        var exception = await Assert.ThrowsAnyAsync<Exception>(async () =>
        {
            await using var client = await McpClient.CreateAsync(transport, new McpClientOptions
            {
                ProtocolVersion = McpProtocolVersions.November2025ProtocolVersion,
            }, loggerFactory: LoggerFactory, cancellationToken: ct);
        });

        Assert.IsType<McpException>(exception);
        Assert.Contains("mismatch", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Client_OnHeaderMismatch_Surfaces_NoFallback()
    {
        // The peer uses per-request metadata (returns the spec-defined -32020 HeaderMismatch on the probe).
        // Falling back to initialize would just produce another malformed envelope.
        // Verify the connect-time logic surfaces the error to the caller instead of falling back.
        var ct = TestContext.Current.CancellationToken;
        await using var transport = new InitializeHandshakeServerTestTransport(
            serverNegotiatedVersion: McpProtocolVersions.November2025ProtocolVersion,
            probeErrorCode: (int)McpErrorCode.HeaderMismatch);

        var exception = await Assert.ThrowsAnyAsync<McpException>(async () =>
        {
            await using var client = await McpClient.CreateAsync(transport, new McpClientOptions
            {
                ProtocolVersion = McpProtocolVersions.July2026ProtocolVersion,
            }, loggerFactory: LoggerFactory, cancellationToken: ct);
        });

        Assert.True(transport.ServerDiscoverProbed);
        Assert.False(transport.InitializeReceived);
        Assert.Equal(McpErrorCode.HeaderMismatch, ((McpProtocolException)exception).ErrorCode);
    }

    [Fact]
    public async Task Client_OnUnsupportedProtocolVersion_WithPerRequestMetadataVersion_RetriesDiscover()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var transport = new PerRequestMetadataRetryTestTransport();

        await using var client = await McpClient.CreateAsync(transport, new McpClientOptions(),
            loggerFactory: LoggerFactory, cancellationToken: ct);

        Assert.Equal(2, transport.ServerDiscoverRequests);
        Assert.False(transport.InitializeReceived);
        Assert.Equal(McpProtocolVersions.July2026ProtocolVersion, client.NegotiatedProtocolVersion);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Client_OnSilentProbe_FallsBackTo_Initialize_AfterConfiguredProbeTimeout(bool infiniteInitialization)
    {
        // Simulate an initialize-handshake server that silently drops the unknown server/discover method (it never
        // responds to the probe). The client must fall back to initialize once the configured
        // DiscoverProbeTimeout elapses, well before the much larger InitializationTimeout.
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        deadline.CancelAfter(TestConstants.DefaultTimeout);
        var ct = deadline.Token;
        await using var transport = new InitializeHandshakeServerTestTransport(
            serverNegotiatedVersion: McpProtocolVersions.November2025ProtocolVersion,
            silentDiscoverProbe: true);

        var timeProvider = new FakeTimeProvider();
        var probeBudget = TimeSpan.FromSeconds(5);
        // Default options (ProtocolVersion = null) prefer 2026-07-28 but allow automatic fallback.
        var connecting = McpClient.CreateAsync(transport, new McpClientOptions
        {
            TimeProvider = timeProvider,
            DiscoverProbeTimeout = probeBudget,
            InitializationTimeout = infiniteInitialization ? Timeout.InfiniteTimeSpan : TestConstants.DefaultTimeout,
        }, loggerFactory: LoggerFactory, cancellationToken: ct);
        await transport.DiscoverReceived.Task.WaitAsync(ct);
        timeProvider.Advance(probeBudget - TimeSpan.FromMilliseconds(1));
        Assert.False(connecting.IsCompleted);
        Assert.False(transport.InitializeReceived);
        timeProvider.Advance(TimeSpan.FromMilliseconds(1));
        await using var client = await connecting.WaitAsync(ct);

        Assert.True(transport.ServerDiscoverProbed);
        Assert.True(transport.InitializeReceived);
        Assert.Equal(McpProtocolVersions.November2025ProtocolVersion, transport.InitializeProtocolVersion);
        Assert.Equal(McpProtocolVersions.November2025ProtocolVersion, client.NegotiatedProtocolVersion);

        Assert.False(ct.IsCancellationRequested);
    }

    [Theory]
    [InlineData(-1, 250)]
    [InlineData(1000, 250)]
    [InlineData(250, 250)]
    public async Task Client_InitializationDeadlineWins_NoFallback(int probeMilliseconds, int initializationMilliseconds)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        deadline.CancelAfter(TestConstants.DefaultTimeout);
        await using var transport = new InitializeHandshakeServerTestTransport(
            McpProtocolVersions.November2025ProtocolVersion, silentDiscoverProbe: true);
        var timeProvider = new FakeTimeProvider();
        var connecting = McpClient.CreateAsync(transport, new McpClientOptions
        {
            TimeProvider = timeProvider,
            DiscoverProbeTimeout = TimeSpan.FromMilliseconds(probeMilliseconds),
            InitializationTimeout = TimeSpan.FromMilliseconds(initializationMilliseconds),
        }, LoggerFactory, deadline.Token);
        await transport.DiscoverReceived.Task.WaitAsync(deadline.Token);
        timeProvider.Advance(TimeSpan.FromMilliseconds(initializationMilliseconds));
        var exception = await Assert.ThrowsAsync<TimeoutException>(() => connecting.WaitAsync(deadline.Token));

        Assert.Equal("Initialization timed out", exception.Message);
        Assert.True(transport.ServerDiscoverProbed);
        Assert.False(transport.InitializeReceived);
        Assert.False(deadline.IsCancellationRequested);
    }

    [Fact]
    public async Task Client_InfiniteProbeAndInitialization_ObserveCallerCancellation()
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        deadline.CancelAfter(TestConstants.DefaultTimeout);
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token);
        await using var transport = new InitializeHandshakeServerTestTransport(
            McpProtocolVersions.November2025ProtocolVersion, silentDiscoverProbe: true);
        var timeProvider = new FakeTimeProvider();
        var connecting = McpClient.CreateAsync(transport, new McpClientOptions
        {
            TimeProvider = timeProvider,
            DiscoverProbeTimeout = Timeout.InfiniteTimeSpan,
            InitializationTimeout = Timeout.InfiniteTimeSpan,
        }, LoggerFactory, caller.Token);
        await transport.DiscoverReceived.Task.WaitAsync(deadline.Token);
        timeProvider.Advance(TimeSpan.FromDays(1));
        Assert.False(connecting.IsCompleted);
        caller.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => connecting);
        Assert.False(transport.InitializeReceived);
    }

    [Fact]
    public async Task Client_PinnedModernVersion_ProbeExpiryDoesNotInitialize()
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        deadline.CancelAfter(TestConstants.DefaultTimeout);
        await using var transport = new InitializeHandshakeServerTestTransport(
            McpProtocolVersions.November2025ProtocolVersion, silentDiscoverProbe: true);
        var timeProvider = new FakeTimeProvider();
        var probeBudget = TimeSpan.FromSeconds(5);
        var connecting = McpClient.CreateAsync(transport, new McpClientOptions
        {
            TimeProvider = timeProvider,
            ProtocolVersion = McpProtocolVersions.July2026ProtocolVersion,
            DiscoverProbeTimeout = probeBudget,
            InitializationTimeout = Timeout.InfiniteTimeSpan,
        }, LoggerFactory, deadline.Token);
        await transport.DiscoverReceived.Task.WaitAsync(deadline.Token);
        timeProvider.Advance(probeBudget);
        await Assert.ThrowsAsync<McpException>(() => connecting.WaitAsync(deadline.Token));
        Assert.True(transport.ServerDiscoverProbed);
        Assert.False(transport.InitializeReceived);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1000)]
    public void DiscoverProbeTimeout_Setter_Rejects_NonPositiveValues(int milliseconds)
    {
        var options = new McpClientOptions();
        Assert.Throws<ArgumentOutOfRangeException>(() => options.DiscoverProbeTimeout = TimeSpan.FromMilliseconds(milliseconds));
    }

    [Fact]
    public async Task Client_RejectsProbeTimeoutBeyondRuntimeTimerRange()
    {
        await using var transport = new InitializeHandshakeServerTestTransport(McpProtocolVersions.November2025ProtocolVersion);
        using var caller = new CancellationTokenSource();
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => McpClient.CreateAsync(transport, new()
        {
            DiscoverProbeTimeout = TimeSpan.MaxValue,
            InitializationTimeout = Timeout.InfiniteTimeSpan,
        }, LoggerFactory, caller.Token));
        caller.Cancel();
        Assert.False(transport.ServerDiscoverProbed);
    }

    [Fact]
    public void DiscoverProbeTimeout_Setter_Accepts_PositiveAndInfiniteValues()
    {
        var options = new McpClientOptions();

        // Default is the documented 5 seconds.
        Assert.Equal(TimeSpan.FromSeconds(5), options.DiscoverProbeTimeout);

        options.DiscoverProbeTimeout = TimeSpan.FromSeconds(30);
        Assert.Equal(TimeSpan.FromSeconds(30), options.DiscoverProbeTimeout);

        // Timeout.InfiniteTimeSpan disables the separate probe timeout (bounded by InitializationTimeout only).
        options.DiscoverProbeTimeout = Timeout.InfiniteTimeSpan;
        Assert.Equal(Timeout.InfiniteTimeSpan, options.DiscoverProbeTimeout);
    }

    [Fact]
    public void TimeProvider_DefaultsToSystem_AndRejectsNull()
    {
        var options = new McpClientOptions();
        Assert.Same(TimeProvider.System, options.TimeProvider);
        Assert.Throws<ArgumentNullException>(() => options.TimeProvider = null!);
    }

    [Fact]
    public async Task Client_LegacyInitialization_UsesTimeProvider()
    {
        var timeProvider = new FakeTimeProvider();
        var timeout = TimeSpan.FromSeconds(10);
        await using var transport = new InitializeHandshakeServerTestTransport(
            McpProtocolVersions.November2025ProtocolVersion, silentInitialize: true);
        var connecting = McpClient.CreateAsync(transport, new()
        {
            TimeProvider = timeProvider,
            ProtocolVersion = McpProtocolVersions.November2025ProtocolVersion,
            InitializationTimeout = timeout,
        }, LoggerFactory, TestContext.Current.CancellationToken);
        await transport.InitializeRequestReceived.Task.WaitAsync(TestConstants.DefaultTimeout, TestContext.Current.CancellationToken);
        timeProvider.Advance(timeout - TimeSpan.FromMilliseconds(1));
        Assert.False(connecting.IsCompleted);
        timeProvider.Advance(TimeSpan.FromMilliseconds(1));

        var error = await Assert.ThrowsAsync<TimeoutException>(() =>
            connecting.WaitAsync(TestConstants.DefaultTimeout, TestContext.Current.CancellationToken));
        Assert.Equal("Initialization timed out", error.Message);
        Assert.False(transport.ServerDiscoverProbed);
    }

    [Fact]
    public async Task Client_CompletedInitialization_IsNotCanceledByAdvancingTime()
    {
        var timeProvider = new FakeTimeProvider();
        await using var transport = new InitializeHandshakeServerTestTransport(McpProtocolVersions.November2025ProtocolVersion);
        await using var client = await McpClient.CreateAsync(transport, new()
        {
            TimeProvider = timeProvider,
        }, LoggerFactory, TestContext.Current.CancellationToken);

        timeProvider.Advance(TimeSpan.FromDays(1));
        await client.PingAsync(cancellationToken: TestContext.Current.CancellationToken).AsTask()
            .WaitAsync(TestConstants.DefaultTimeout, TestContext.Current.CancellationToken);
        Assert.False(client.Completion.IsCompleted);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, HttpTransportMode.StreamableHttp)]
    [InlineData(HttpStatusCode.NotFound, HttpTransportMode.AutoDetect)]
    [InlineData(HttpStatusCode.BadRequest, HttpTransportMode.StreamableHttp)]
    [InlineData(HttpStatusCode.BadRequest, HttpTransportMode.AutoDetect)]
    public async Task Client_OnFallbackHttpStatusFromProbe_FallsBackTo_Initialize(
        HttpStatusCode status, HttpTransportMode transportMode)
    {
        // A server predating SEP-2575 can reject the session-less server/discover probe at the HTTP layer
        // rather than with a JSON-RPC error: 404 when it requires Mcp-Session-Id on every non-initialize
        // POST, or a plain/empty 400 when it cannot parse the request. Both are initialize-handshake
        // servers, so the connect must fall back instead of failing. (405 is deliberately excluded: the
        // POST endpoint rejecting the request method does not mean initialize will succeed over the same
        // transport, and the spec routes 405 to the AutoDetect transport's SSE fallback — see
        // Client_On405FromProbe_DoesNotFallBackTo_Initialize.)
        var ct = TestContext.Current.CancellationToken;
        var initializeReceived = false;

        using var mockHttpHandler = new MockHttpHandler();
        using var httpClient = new HttpClient(mockHttpHandler);
        mockHttpHandler.RequestHandler = CreateProbeRejectingServer(
            status, "Invalid session ID", () => initializeReceived = true);

        await using var transport = CreateTransport(httpClient, transportMode);

        // Default options (ProtocolVersion = null) prefer 2026-07-28 but allow automatic fallback.
        await using var client = await McpClient.CreateAsync(transport, new McpClientOptions(),
            loggerFactory: LoggerFactory, cancellationToken: ct);

        Assert.True(initializeReceived);
        Assert.Equal(McpProtocolVersions.November2025ProtocolVersion, client.NegotiatedProtocolVersion);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, HttpTransportMode.StreamableHttp)]
    [InlineData(HttpStatusCode.BadRequest, HttpTransportMode.AutoDetect)]
    [InlineData(HttpStatusCode.NotFound, HttpTransportMode.StreamableHttp)]
    [InlineData(HttpStatusCode.NotFound, HttpTransportMode.AutoDetect)]
    public async Task Client_OnStructuredFallbackHttpStatusFromProbe_FallsBackTo_Initialize(
        HttpStatusCode status, HttpTransportMode transportMode)
    {
        var ct = TestContext.Current.CancellationToken;
        var initializeReceived = false;

        using var mockHttpHandler = new MockHttpHandler();
        using var httpClient = new HttpClient(mockHttpHandler);
        mockHttpHandler.RequestHandler = CreateStructuredProbeRejectingServer(
            status,
            () => initializeReceived = true);

        await using var transport = CreateTransport(httpClient, transportMode);
        await using var client = await McpClient.CreateAsync(transport, new McpClientOptions(),
            loggerFactory: LoggerFactory, cancellationToken: ct);

        Assert.True(initializeReceived);
        Assert.Equal(McpProtocolVersions.November2025ProtocolVersion, client.NegotiatedProtocolVersion);
    }

    [Theory]
    [InlineData(HttpTransportMode.StreamableHttp, false)]
    [InlineData(HttpTransportMode.AutoDetect, true)]
    public async Task Client_On405FromProbe_DoesNotFallBackTo_Initialize(
        HttpTransportMode transportMode, bool expectSseAttempt)
    {
        // 405 means the POST endpoint rejected the request method, so retrying initialize over the same
        // transport is not useful. The spec routes 405 to the AutoDetect transport's SSE fallback: in
        // Streamable HTTP mode the 405 surfaces directly (no GET), and in AutoDetect mode the client
        // attempts the deprecated SSE GET. Assert GET/no-GET explicitly; do not treat "no initialize"
        // as a general invariant after a successful SSE selection (revised #1719 will handshake then).
        var ct = TestContext.Current.CancellationToken;
        var initializeReceived = false;
        var sseRequested = false;

        using var mockHttpHandler = new MockHttpHandler();
        using var httpClient = new HttpClient(mockHttpHandler);
        mockHttpHandler.RequestHandler = CreateProbeRejectingServer(
            HttpStatusCode.MethodNotAllowed, "Invalid session ID",
            () => initializeReceived = true, () => sseRequested = true);

        await using var transport = CreateTransport(httpClient, transportMode);

        await Assert.ThrowsAnyAsync<HttpRequestException>(async () =>
        {
            await using var client = await McpClient.CreateAsync(transport, new McpClientOptions(),
                loggerFactory: LoggerFactory, cancellationToken: ct);
        });

        Assert.Equal(expectSseAttempt, sseRequested);
        if (transportMode == HttpTransportMode.StreamableHttp)
        {
            // Explicit Streamable HTTP has no SSE path, so initialize must not be used to recover a 405.
            Assert.False(initializeReceived);
        }
    }

    [Theory]
    [InlineData(HttpTransportMode.StreamableHttp, false)]
    [InlineData(HttpTransportMode.AutoDetect, true)]
    public async Task Client_OnStructured405FromProbe_TriesSseOnlyInAutoDetect(
        HttpTransportMode transportMode, bool expectSseAttempt)
    {
        // An unrecognized JSON-RPC error on 405 is not a recognized modern signal, so AutoDetect must
        // classify the full response and try SSE. Explicit Streamable HTTP surfaces the 405 with no GET.
        var ct = TestContext.Current.CancellationToken;
        var initializeReceived = false;
        var sseRequested = false;

        using var mockHttpHandler = new MockHttpHandler();
        using var httpClient = new HttpClient(mockHttpHandler);
        mockHttpHandler.RequestHandler = CreateStructuredProbeRejectingServer(
            HttpStatusCode.MethodNotAllowed,
            () => initializeReceived = true,
            () => sseRequested = true);

        await using var transport = CreateTransport(httpClient, transportMode);

        await Assert.ThrowsAnyAsync<HttpRequestException>(async () =>
        {
            await using var client = await McpClient.CreateAsync(transport, new McpClientOptions(),
                loggerFactory: LoggerFactory, cancellationToken: ct);
        });

        Assert.Equal(expectSseAttempt, sseRequested);
        if (transportMode == HttpTransportMode.StreamableHttp)
        {
            Assert.False(initializeReceived);
        }
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError, HttpTransportMode.StreamableHttp)]
    [InlineData(HttpStatusCode.Forbidden, HttpTransportMode.StreamableHttp)]
    [InlineData(HttpStatusCode.UnsupportedMediaType, HttpTransportMode.StreamableHttp)]
    [InlineData(HttpStatusCode.InternalServerError, HttpTransportMode.AutoDetect)]
    [InlineData(HttpStatusCode.Unauthorized, HttpTransportMode.AutoDetect)]
    [InlineData(HttpStatusCode.Forbidden, HttpTransportMode.AutoDetect)]
    [InlineData(HttpStatusCode.UnsupportedMediaType, HttpTransportMode.AutoDetect)]
    public async Task Client_OnOtherHttpErrorFromProbe_Surfaces_NoFallback(
        HttpStatusCode status, HttpTransportMode transportMode)
    {
        // Non-allowlisted statuses (401/403/5xx/415/…) must surface directly for both structured and
        // unstructured bodies — no initialize handshake and no deprecated SSE GET.
        var ct = TestContext.Current.CancellationToken;
        var initializeReceived = false;
        var sseRequested = false;

        using var mockHttpHandler = new MockHttpHandler();
        using var httpClient = new HttpClient(mockHttpHandler);
        mockHttpHandler.RequestHandler = CreateProbeRejectingServer(
            status, "nope", () => initializeReceived = true, () => sseRequested = true);

        await using var transport = CreateTransport(httpClient, transportMode);

        await Assert.ThrowsAnyAsync<HttpRequestException>(async () =>
        {
            await using var client = await McpClient.CreateAsync(transport, new McpClientOptions(),
                loggerFactory: LoggerFactory, cancellationToken: ct);
        });

        Assert.False(initializeReceived);
        Assert.False(sseRequested);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError, HttpTransportMode.StreamableHttp)]
    [InlineData(HttpStatusCode.Forbidden, HttpTransportMode.StreamableHttp)]
    [InlineData(HttpStatusCode.UnsupportedMediaType, HttpTransportMode.StreamableHttp)]
    [InlineData(HttpStatusCode.InternalServerError, HttpTransportMode.AutoDetect)]
    [InlineData(HttpStatusCode.Unauthorized, HttpTransportMode.AutoDetect)]
    [InlineData(HttpStatusCode.Forbidden, HttpTransportMode.AutoDetect)]
    [InlineData(HttpStatusCode.UnsupportedMediaType, HttpTransportMode.AutoDetect)]
    public async Task Client_OnStructuredOtherHttpErrorFromProbe_Surfaces_NoGet(
        HttpStatusCode status, HttpTransportMode transportMode)
    {
        // Same non-allowlist coverage for unrecognized JSON-RPC-bodied responses: no SSE GET.
        var ct = TestContext.Current.CancellationToken;
        var initializeReceived = false;
        var sseRequested = false;

        using var mockHttpHandler = new MockHttpHandler();
        using var httpClient = new HttpClient(mockHttpHandler);
        mockHttpHandler.RequestHandler = CreateStructuredProbeRejectingServer(
            status, () => initializeReceived = true, () => sseRequested = true);

        await using var transport = CreateTransport(httpClient, transportMode);

        await Assert.ThrowsAnyAsync<HttpRequestException>(async () =>
        {
            await using var client = await McpClient.CreateAsync(transport, new McpClientOptions(),
                loggerFactory: LoggerFactory, cancellationToken: ct);
        });

        Assert.False(initializeReceived);
        Assert.False(sseRequested);
    }

    private HttpClientTransport CreateTransport(HttpClient httpClient, HttpTransportMode transportMode)
        => new(new HttpClientTransportOptions
        {
            Endpoint = new Uri("http://localhost"),
            TransportMode = transportMode,
            Name = "HTTP discover probe test client",
        }, httpClient, LoggerFactory);

    /// <summary>
    /// Mock Streamable HTTP server that rejects <c>server/discover</c> with <paramref name="probeStatus"/>
    /// and, if the client falls back, completes an <c>initialize</c> handshake at 2025-11-25.
    /// </summary>
    private static Func<HttpRequestMessage, Task<HttpResponseMessage>> CreateProbeRejectingServer(
        HttpStatusCode probeStatus, string probeBody, Action onInitialize, Action? onSseRequest = null)
        => async request =>
        {
            // The server offers no standalone SSE stream, which the spec permits.
            // net472 does not populate a default Content, so every response sets one explicitly.
            if (request.Method == HttpMethod.Get)
            {
                // Track accidental AutoDetect fallback for non-allowlisted HTTP failures.
                onSseRequest?.Invoke();
                return EmptyResponse(HttpStatusCode.MethodNotAllowed);
            }

            var body = await request.Content!.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(body);
            if (!doc.RootElement.TryGetProperty("method", out var methodElement))
                return EmptyResponse(HttpStatusCode.Accepted);

            switch (methodElement.GetString())
            {
                case RequestMethods.ServerDiscover:
                    return new HttpResponseMessage(probeStatus) { Content = new StringContent(probeBody) };

                case RequestMethods.Initialize:
                    onInitialize();
                    var id = doc.RootElement.GetProperty("id").GetRawText();
                    var result = "{\"jsonrpc\":\"2.0\",\"id\":" + id
                        + ",\"result\":{\"protocolVersion\":\"" + McpProtocolVersions.November2025ProtocolVersion
                        + "\",\"capabilities\":{},\"serverInfo\":{\"name\":\"test\",\"version\":\"1.0\"}}}";
                    var response = new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(result, Encoding.UTF8, "application/json"),
                    };
                    response.Headers.Add("mcp-session-id", "test-session");
                    return response;

                default:
                    return EmptyResponse(HttpStatusCode.Accepted);
            }
        };

    private static Func<HttpRequestMessage, Task<HttpResponseMessage>> CreateStructuredProbeRejectingServer(
        HttpStatusCode probeStatus, Action onInitialize, Action? onSseRequest = null)
        => async request =>
        {
            if (request.Method == HttpMethod.Get)
            {
                onSseRequest?.Invoke();
                return EmptyResponse(HttpStatusCode.MethodNotAllowed);
            }

            var body = await request.Content!.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(body);
            if (!doc.RootElement.TryGetProperty("method", out var methodElement))
                return EmptyResponse(HttpStatusCode.Accepted);

            if (methodElement.GetString() == RequestMethods.ServerDiscover)
            {
                var id = doc.RootElement.GetProperty("id").GetRawText();
                var error = "{\"jsonrpc\":\"2.0\",\"id\":" + id
                    + ",\"error\":{\"code\":-32600,\"message\":\"Mcp-Session-Id header is required\"}}";
                return new HttpResponseMessage(probeStatus)
                {
                    Content = new StringContent(error, Encoding.UTF8, "application/json"),
                };
            }

            if (methodElement.GetString() == RequestMethods.Initialize)
            {
                onInitialize();
                var id = doc.RootElement.GetProperty("id").GetRawText();
                var result = "{\"jsonrpc\":\"2.0\",\"id\":" + id
                    + ",\"result\":{\"protocolVersion\":\"" + McpProtocolVersions.November2025ProtocolVersion
                    + "\",\"capabilities\":{},\"serverInfo\":{\"name\":\"test\",\"version\":\"1.0\"}}}";
                var response = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(result, Encoding.UTF8, "application/json"),
                };
                response.Headers.Add("mcp-session-id", "test-session");
                return response;
            }

            return EmptyResponse(HttpStatusCode.Accepted);
        };

    private static HttpResponseMessage EmptyResponse(HttpStatusCode status)
        => new(status) { Content = new StringContent(string.Empty) };

    /// <summary>
    /// Minimal in-memory transport that simulates an initialize-handshake server: rejects
    /// <c>server/discover</c> (with a configurable JSON-RPC error code, or by
    /// silently dropping the request) and responds to <c>initialize</c> with a
    /// configurable protocol version.
    /// </summary>
    private sealed class InitializeHandshakeServerTestTransport(
        string serverNegotiatedVersion,
        int probeErrorCode = (int)McpErrorCode.MethodNotFound,
        bool silentDiscoverProbe = false,
        bool silentInitialize = false) : IClientTransport
    {
        private readonly Channel<JsonRpcMessage> _incomingToClient = Channel.CreateUnbounded<JsonRpcMessage>();

        public string Name => "initialize-handshake-server-test-transport";

        public bool ServerDiscoverProbed { get; private set; }

        public TaskCompletionSource<bool> DiscoverReceived { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool InitializeReceived { get; private set; }

        public TaskCompletionSource<bool> InitializeRequestReceived { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public string? InitializeProtocolVersion { get; private set; }

        public Task<ITransport> ConnectAsync(CancellationToken cancellationToken = default)
        {
            ITransport transport = new TransportChannel(_incomingToClient, this);
            return Task.FromResult(transport);
        }

        public ValueTask DisposeAsync() => default;

        private void HandleOutgoingMessage(JsonRpcMessage message)
        {
            switch (message)
            {
                case JsonRpcRequest { Method: RequestMethods.ServerDiscover } discoverReq:
                    ServerDiscoverProbed = true;
                    DiscoverReceived.TrySetResult(true);
                    if (silentDiscoverProbe)
                    {
                        // Model an initialize-handshake server that drops the unknown method without replying.
                        break;
                    }

                    _ = WriteAsync(new JsonRpcError
                    {
                        Id = discoverReq.Id,
                        Error = new JsonRpcErrorDetail
                        {
                            Code = probeErrorCode,
                            Message = probeErrorCode == (int)McpErrorCode.MethodNotFound
                                ? "Method not found"
                                : "Invalid params",
                        },
                    });
                    break;

                case JsonRpcRequest { Method: RequestMethods.Initialize } initReq:
                    InitializeReceived = true;
                    InitializeRequestReceived.TrySetResult(true);
                    if (silentInitialize)
                    {
                        break;
                    }
                    var initializeRequest = JsonSerializer.Deserialize<InitializeRequestParams>(initReq.Params, McpJsonUtilities.DefaultOptions);
                    InitializeProtocolVersion = initializeRequest?.ProtocolVersion;
                    _ = WriteAsync(new JsonRpcResponse
                    {
                        Id = initReq.Id,
                        Result = JsonSerializer.SerializeToNode(new InitializeResult
                        {
                            ProtocolVersion = serverNegotiatedVersion,
                            Capabilities = new ServerCapabilities(),
                            ServerInfo = new Implementation { Name = "initialize-handshake-test-server", Version = "1.0.0" },
                        }, McpJsonUtilities.DefaultOptions),
                    });
                    break;

                case JsonRpcRequest { Method: RequestMethods.Ping } ping:
                    _ = WriteAsync(new JsonRpcResponse { Id = ping.Id, Result = new JsonObject() });
                    break;
            }
        }

        private Task WriteAsync(JsonRpcMessage message)
            => _incomingToClient.Writer.WriteAsync(message, CancellationToken.None).AsTask();

        private sealed class TransportChannel(
            Channel<JsonRpcMessage> incoming,
            InitializeHandshakeServerTestTransport parent) : ITransport
        {
            public ChannelReader<JsonRpcMessage> MessageReader => incoming.Reader;
            public bool IsConnected { get; private set; } = true;
            public string? SessionId => null;

            public Task SendMessageAsync(JsonRpcMessage message, CancellationToken cancellationToken = default)
            {
                parent.HandleOutgoingMessage(message);
                return Task.CompletedTask;
            }

            public ValueTask DisposeAsync()
            {
                incoming.Writer.TryComplete();
                IsConnected = false;
                return default;
            }
        }
    }

    private sealed class PerRequestMetadataRetryTestTransport(IList<string>? supportedVersions = null) : IClientTransport
    {
        private readonly Channel<JsonRpcMessage> _incomingToClient = Channel.CreateUnbounded<JsonRpcMessage>();

        public string Name => "per-request-metadata-retry-test-transport";

        public int ServerDiscoverRequests { get; private set; }

        public bool InitializeReceived { get; private set; }

        public Task<ITransport> ConnectAsync(CancellationToken cancellationToken = default)
        {
            ITransport transport = new TransportChannel(_incomingToClient, this);
            return Task.FromResult(transport);
        }

        public ValueTask DisposeAsync() => default;

        private void HandleOutgoingMessage(JsonRpcMessage message)
        {
            switch (message)
            {
                case JsonRpcRequest { Method: RequestMethods.ServerDiscover } discoverReq:
                    ServerDiscoverRequests++;

                    if (ServerDiscoverRequests == 1)
                    {
                        _ = WriteAsync(new JsonRpcError
                        {
                            Id = discoverReq.Id,
                            Error = new JsonRpcErrorDetail
                            {
                                Code = (int)McpErrorCode.UnsupportedProtocolVersion,
                                Message = "Unsupported protocol version",
                                Data = CreateUnsupportedProtocolVersionData(),
                            },
                        });
                    }
                    else
                    {
                        _ = WriteAsync(new JsonRpcResponse
                        {
                            Id = discoverReq.Id,
                            Result = JsonSerializer.SerializeToNode(new DiscoverResult
                            {
                                SupportedVersions = [McpProtocolVersions.July2026ProtocolVersion],
                                Capabilities = new ServerCapabilities(),
                                Meta = new JsonObject
                                {
                                    [MetaKeys.ServerInfo] = JsonSerializer.SerializeToNode(new Implementation { Name = "per-request-metadata-test-server", Version = "1.0.0" }, McpJsonUtilities.DefaultOptions),
                                },
                            }, McpJsonUtilities.DefaultOptions),
                        });
                    }

                    break;

                case JsonRpcRequest { Method: RequestMethods.Initialize }:
                    InitializeReceived = true;
                    break;
            }
        }

        private Task WriteAsync(JsonRpcMessage message)
            => _incomingToClient.Writer.WriteAsync(message, CancellationToken.None).AsTask();

        private JsonElement CreateUnsupportedProtocolVersionData()
        {
            var json = JsonSerializer.Serialize(new UnsupportedProtocolVersionErrorData
            {
                Requested = McpProtocolVersions.July2026ProtocolVersion,
                Supported = supportedVersions ?? [McpProtocolVersions.July2026ProtocolVersion],
            }, McpJsonUtilities.DefaultOptions);

            using var document = JsonDocument.Parse(json);
            return document.RootElement.Clone();
        }

        private sealed class TransportChannel(
            Channel<JsonRpcMessage> incoming,
            PerRequestMetadataRetryTestTransport parent) : ITransport
        {
            public ChannelReader<JsonRpcMessage> MessageReader => incoming.Reader;
            public bool IsConnected { get; private set; } = true;
            public string? SessionId => null;

            public Task SendMessageAsync(JsonRpcMessage message, CancellationToken cancellationToken = default)
            {
                parent.HandleOutgoingMessage(message);
                return Task.CompletedTask;
            }

            public ValueTask DisposeAsync()
            {
                incoming.Writer.TryComplete();
                IsConnected = false;
                return default;
            }
        }
    }
}
