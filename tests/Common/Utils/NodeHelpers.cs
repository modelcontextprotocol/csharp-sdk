using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace ModelContextProtocol.Tests.Utils;

/// <summary>
/// Helper utilities for Node.js and npm operations.
/// </summary>
public static class NodeHelpers
{
    private static readonly object _npmInstallLock = new();
    private static bool _npmInstallCompleted;

    /// <summary>
    /// Finds the repository root by searching for package-lock.json in ancestor directories.
    /// </summary>
    private static string FindRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir, "package-lock.json")))
            {
                return dir;
            }
            dir = Path.GetDirectoryName(dir);
        }

        throw new InvalidOperationException("Could not find repository root (no package-lock.json found in ancestor directories).");
    }

    /// <summary>
    /// Ensures npm dependencies are installed by running 'npm ci' from the repo root.
    /// This is safe to call multiple times; it only runs once per test process.
    /// </summary>
    public static void EnsureNpmDependenciesInstalled()
    {
        if (_npmInstallCompleted)
        {
            return;
        }

        lock (_npmInstallLock)
        {
            if (_npmInstallCompleted)
            {
                return;
            }

            var repoRoot = FindRepoRoot();
            var nodeModulesPath = Path.Combine(repoRoot, "node_modules");
            var lockFilePath = Path.Combine(repoRoot, "package-lock.json");

            // Run 'npm ci' if node_modules doesn't exist or is outdated
            // (package-lock.json is newer than node_modules).
            if (!Directory.Exists(nodeModulesPath) ||
                File.GetLastWriteTimeUtc(lockFilePath) > Directory.GetLastWriteTimeUtc(nodeModulesPath))
            {
                var startInfo = NpmStartInfo("ci", repoRoot);
                using var process = Process.Start(startInfo)
                    ?? throw new InvalidOperationException("Failed to start 'npm ci'.");
                process.WaitForExit(120_000);

                if (process.ExitCode != 0)
                {
                    var error = process.StandardError.ReadToEnd();
                    throw new InvalidOperationException($"'npm ci' failed with exit code {process.ExitCode}: {error}");
                }
            }

            _npmInstallCompleted = true;
        }
    }

    /// <summary>
    /// Creates a ProcessStartInfo configured to run a binary from node_modules/.bin/conformance.
    /// Calls <see cref="EnsureNpmDependenciesInstalled"/> first.
    /// </summary>
    /// <param name="binaryName">The name of the binary in node_modules/.bin (e.g. "conformance").</param>
    /// <param name="arguments">The arguments to pass to the binary.</param>
    /// <param name="appendProtocolVersionFromEnv">
    /// When <see langword="true"/> (the default) and the MCP_CONFORMANCE_PROTOCOL_VERSION
    /// environment variable is set, a "--spec-version &lt;value&gt;" argument is appended.
    /// Pass <see langword="false"/> for scenarios that pin their own spec version (e.g. the
    /// caching scenario specific to the 2026-07-28 protocol) to avoid a conflicting duplicate flag.
    /// </param>
    /// <returns>A configured ProcessStartInfo for running the binary.</returns>
    public static ProcessStartInfo ConformanceTestStartInfo(string arguments, bool appendProtocolVersionFromEnv = true)
    {
        EnsureNpmDependenciesInstalled();

        // If MCP_CONFORMANCE_PROTOCOL_VERSION is set, pass it as --spec-version to the runner.
        if (appendProtocolVersionFromEnv)
        {
            var protocolVersion = Environment.GetEnvironmentVariable("MCP_CONFORMANCE_PROTOCOL_VERSION");
            if (!string.IsNullOrEmpty(protocolVersion))
            {
                arguments += $" --spec-version {protocolVersion}";
            }
        }

        var repoRoot = FindRepoRoot();
        var binPath = Path.Combine(repoRoot, "node_modules", ".bin", "conformance");

        ProcessStartInfo startInfo;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // On Windows, node_modules/.bin contains .cmd shims that can be executed directly
            startInfo = new ProcessStartInfo
            {
                FileName = $"{binPath}.cmd",
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
        }
        else
        {
            startInfo = new ProcessStartInfo
            {
                FileName = binPath,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
        }

        // On macOS, disable .NET mini-dump file generation for child processes. When
        // dotnet test runs with --blame-crash, it sets DOTNET_DbgEnableMiniDump=1 in the
        // environment. This is inherited by grandchild .NET processes (e.g. ConformanceClient
        // launched via node). On macOS, the createdump tool can hang indefinitely due to
        // ptrace/SIP restrictions, causing the entire test run to hang. Disabling mini-dumps
        // only suppresses the dump file creation; the runtime still prints crash diagnostics
        // (stack traces, signal info, etc.) to stderr, which the test captures.
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            startInfo.Environment["DOTNET_DbgEnableMiniDump"] = "0";
            startInfo.Environment["COMPlus_DbgEnableMiniDump"] = "0";
        }

        return startInfo;
    }

    /// <summary>
    /// Checks if Node.js is installed and available on the system.
    /// </summary>
    public static bool IsNodeInstalled()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "node",
                Arguments = "--version",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });

            if (process == null)
            {
                return false;
            }

            process.WaitForExit(5000);
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Checks whether the SEP-2243 conformance scenarios are available in the installed
    /// conformance package.
    /// </summary>
    public static bool HasSep2243Scenarios()
        => HasInstalledConformanceScenarios(
            "http-standard-headers",
            "http-invalid-tool-headers",
            "http-header-validation",
            "http-custom-header-server-validation");

    /// <summary>
    /// Checks whether the SEP-2575 request-metadata client conformance scenario is available
    /// in the installed conformance package.
    /// </summary>
    public static bool HasRequestMetadataScenario()
        => HasInstalledConformanceScenario("request-metadata");

    /// <summary>
    /// Checks whether the SEP-2549 "caching" conformance scenario is available in the installed
    /// conformance package.
    /// </summary>
    public static bool HasCachingScenario()
        => HasInstalledConformanceScenario("caching");

    /// <summary>
    /// Checks whether all named conformance scenarios are present in the installed
    /// <c>@modelcontextprotocol/conformance</c> bundle. This is intentionally based on the
    /// installed scenario list rather than the package version so prerelease/private builds are
    /// gated by the scenarios they actually contain.
    /// </summary>
    private static bool HasInstalledConformanceScenarios(params string[] scenarioNames)
        => ReadInstalledConformanceBundle() is { } bundle
            && scenarioNames.All(scenarioName => HasInstalledConformanceScenario(bundle, scenarioName));

    private static bool HasInstalledConformanceScenario(string scenarioName)
        => ReadInstalledConformanceBundle() is { } bundle
            && HasInstalledConformanceScenario(bundle, scenarioName);

    private static bool HasInstalledConformanceScenario(string bundle, string scenarioName)
        => bundle.Contains($"`{scenarioName}`", StringComparison.Ordinal) ||
           bundle.Contains($"\"{scenarioName}\"", StringComparison.Ordinal) ||
           bundle.Contains($"'{scenarioName}'", StringComparison.Ordinal);

    private static string? ReadInstalledConformanceBundle()
    {
        try
        {
            var repoRoot = FindRepoRoot();
            var bundlePath = Path.Combine(
                repoRoot, "node_modules", "@modelcontextprotocol", "conformance", "dist", "index.js");

            // This is a skip gate for scenario-conditional conformance tests, so it must stay
            // side-effect-free. If the conformance package isn't installed, report no bundle (the
            // scenario is simply gated off); the actual scenario run path restores npm dependencies
            // separately via ConformanceTestStartInfo.
            if (!File.Exists(bundlePath))
            {
                return null;
            }

            return File.ReadAllText(bundlePath);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Runs the conformance runner ("conformance &lt;arguments&gt;") in server mode and returns
    /// whether it succeeded along with the captured stdout/stderr. Centralizes the process
    /// plumbing (output capture, a 5-minute timeout, and the Windows libuv-shutdown fallback)
    /// shared by the server-side conformance tests.
    /// </summary>
    /// <param name="arguments">Arguments to pass to the conformance runner.</param>
    /// <param name="onLine">Optional callback invoked for each captured stdout/stderr line.</param>
    /// <param name="appendProtocolVersionFromEnv">
    /// Forwarded to <see cref="ConformanceTestStartInfo(string, bool)"/>.
    /// </param>
    /// <param name="cancellationToken">Token used to cancel the run.</param>
    public static async Task<(bool Success, string Output, string Error)> RunServerConformanceAsync(
        string arguments,
        Action<string>? onLine = null,
        bool appendProtocolVersionFromEnv = true,
        CancellationToken cancellationToken = default)
    {
        var startInfo = ConformanceTestStartInfo(arguments, appendProtocolVersionFromEnv);

        var outputBuilder = new StringBuilder();
        var errorBuilder = new StringBuilder();

        using var process = new Process { StartInfo = startInfo };

        // Protect callbacks with try/catch so a callback that throws on a background thread
        // (e.g. ITestOutputHelper after the test completes) does not crash the test host.
        DataReceivedEventHandler outputHandler = (sender, e) =>
        {
            if (e.Data != null)
            {
                try { onLine?.Invoke(e.Data); } catch { }
                outputBuilder.AppendLine(e.Data);
            }
        };

        DataReceivedEventHandler errorHandler = (sender, e) =>
        {
            if (e.Data != null)
            {
                try { onLine?.Invoke(e.Data); } catch { }
                errorBuilder.AppendLine(e.Data);
            }
        };

        process.OutputDataReceived += outputHandler;
        process.ErrorDataReceived += errorHandler;

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromMinutes(5));
        try
        {
#if NET
            await process.WaitForExitAsync(cts.Token);
#else
            // net472 lacks the CancellationToken overload; fall back to the timeout-based polyfill
            // extension and surface a timeout the same way the current target-framework path does.
            await process.WaitForExitAsync(TimeSpan.FromMinutes(5));
            if (!process.HasExited)
            {
                throw new OperationCanceledException();
            }
#endif
        }
        catch (OperationCanceledException)
        {
#if NET
            process.Kill(entireProcessTree: true);
#else
            process.Kill();
#endif
            process.OutputDataReceived -= outputHandler;
            process.ErrorDataReceived -= errorHandler;
            return (
                false,
                outputBuilder.ToString(),
                errorBuilder.ToString() + "\nProcess timed out after 5 minutes and was killed.");
        }

        process.OutputDataReceived -= outputHandler;
        process.ErrorDataReceived -= errorHandler;

        var stdoutText = outputBuilder.ToString();
        var stderrText = errorBuilder.ToString();

        // The Node.js conformance runner can crash during cleanup on Windows with a libuv
        // assertion ("!(handle->flags & UV_HANDLE_CLOSING)") that produces a non-zero exit
        // code even though every conformance check passed. When that happens, fall back to
        // parsing the "Test Results:" summary in stdout to decide success.
        bool success = process.ExitCode == 0 || ConformanceOutputIndicatesSuccess(stdoutText);

        return (success, stdoutText, stderrText);
    }

    /// <summary>
    /// Parses the conformance runner output for a "Test Results:" line such as
    /// "Passed: 3/3, 0 failed, 0 warnings" and returns true when all checks passed
    /// and none failed.
    /// </summary>
    private static bool ConformanceOutputIndicatesSuccess(string output)
    {
        // Match lines like "Passed: 3/3, 0 failed, 0 warnings"
        var match = Regex.Match(output, @"Passed:\s*(\d+)/(\d+),\s*(\d+)\s*failed");
        if (!match.Success)
        {
            return false;
        }

        int passed = int.Parse(match.Groups[1].Value);
        int total = int.Parse(match.Groups[2].Value);
        int failed = int.Parse(match.Groups[3].Value);

        return passed == total && failed == 0 && total > 0;
    }

    /// <summary>
    /// Checks whether the SEP-2322 (Multi Round-Trip Requests / InputRequiredResult)
    /// conformance scenarios are available in the installed conformance package.
    /// </summary>
    public static bool HasMrtrScenarios()
        => HasInstalledConformanceScenarios(
            "input-required-result-basic-elicitation",
            "input-required-result-basic-sampling",
            "input-required-result-basic-list-roots",
            "input-required-result-request-state",
            "input-required-result-multiple-input-requests",
            "input-required-result-multi-round",
            "input-required-result-missing-input-response",
            "input-required-result-non-tool-request",
            "input-required-result-result-type",
            "input-required-result-unsupported-methods",
            "input-required-result-tampered-state",
            "input-required-result-capability-check",
            "input-required-result-ignore-extra-params",
            "input-required-result-validate-input");

    private static ProcessStartInfo NpmStartInfo(string arguments, string workingDirectory)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c npm {arguments}",
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
        }
        else
        {
            return new ProcessStartInfo
            {
                FileName = "npm",
                Arguments = arguments,
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
        }
    }
}
