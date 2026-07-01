using ModelContextProtocol.Tests.Utils;

namespace ModelContextProtocol.ConformanceTests;

/// <summary>
/// Runs the official MCP conformance "caching" scenario (SEP-2549: TTL for List Results,
/// added in conformance PR #275) against the SDK's ConformanceServer, verifying that the SDK
/// correctly emits the <c>ttlMs</c> and <c>cacheScope</c> caching hints on cacheable results
/// (tools/list, prompts/list, resources/list, resources/templates/list, resources/read).
/// </summary>
/// <remarks>
/// The scenario was introduced in spec wire version 2026-07-28 and uses the stateless lifecycle,
/// so it runs against the shared server's stateless endpoint
/// (<see cref="ConformanceServerFixture.StatelessServerUrl"/>). It is gated on the installed
/// conformance package version (>= 0.2.0).
/// </remarks>
[Collection(nameof(ConformanceServerCollection))]
public class CachingConformanceTests(ConformanceServerFixture fixture, ITestOutputHelper output)
{
    [Fact]
    public async Task RunCachingConformanceTest()
    {
        Assert.SkipWhen(!NodeHelpers.IsNodeInstalled(), "Node.js is not installed. Skipping conformance tests.");
        Assert.SkipWhen(
            !NodeHelpers.HasCachingScenario(),
            "SEP-2549 caching conformance scenario not available (requires conformance package >= 0.2.0).");

        // The caching scenario only exists in the 2026-07-28 protocol revision, so pin the spec version
        // explicitly (and suppress the MCP_CONFORMANCE_PROTOCOL_VERSION override to avoid a
        // conflicting duplicate --spec-version flag).
        var result = await NodeHelpers.RunServerConformanceAsync(
            $"server --url {fixture.StatelessServerUrl} --scenario caching --spec-version 2026-07-28",
            line => { try { output.WriteLine(line); } catch { } },
            appendProtocolVersionFromEnv: false,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.Success,
            $"SEP-2549 caching conformance test failed.\n\nStdout:\n{result.Output}\n\nStderr:\n{result.Error}");
    }
}
