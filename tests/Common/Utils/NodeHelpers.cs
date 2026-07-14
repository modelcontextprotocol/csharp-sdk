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
    /// Checks whether the installed conformance package contains a spec-conformant
    /// <c>http-custom-headers</c> scenario. Prereleases 0.2.0-alpha.5 through 0.2.0-alpha.7
    /// annotated a <c>number</c>-typed parameter with <c>x-mcp-header</c>, which SEP-2243
    /// forbids; a conformant client excludes that tool, so every positive check in the
    /// scenario fails. Conformance PR #371 fixed the scenario and shipped it in 0.2.0-alpha.8,
    /// so this gate requires at least that version. Unlike <see cref="HasSep2243Scenarios"/>,
    /// this comparison honors the semver prerelease so older 0.2.0 prereleases are skipped
    /// rather than failing spuriously.
    /// </summary>
    public static bool HasConformantCustomHeadersScenario()
        => IsInstalledConformanceVersionAtLeast("0.2.0-alpha.8");

    /// <summary>
    /// Checks whether the SEP-2549 "caching" conformance scenario (added in conformance
    /// PR #275) is available, by reading the <em>installed</em> conformance package version
    /// from node_modules. The caching scenario was introduced in conformance package 0.2.0.
    /// Reading the installed version (rather than the pinned version in package.json) means
    /// this also returns <see langword="true"/> when a newer private build has been installed
    /// locally via <c>npm install --no-save &lt;path-to-conformance&gt;</c>.
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
    /// Returns <see langword="true"/> when the conformance package installed in node_modules
    /// has a semver precedence greater than or equal to <paramref name="minimumVersion"/>,
    /// honoring the prerelease component (e.g. "0.2.0-alpha.8"). Returns <see langword="false"/>
    /// when no version can be determined.
    /// </summary>
    private static bool IsInstalledConformanceVersionAtLeast(string minimumVersion)
    {
        var installed = GetInstalledConformanceVersionString();
        return installed is not null && CompareSemVer(installed, minimumVersion) >= 0;
    }

    /// <summary>
    /// Reads the raw version string of the conformance package installed in node_modules,
    /// preserving any prerelease/build suffix. Returns <see langword="null"/> if it cannot be
    /// determined.
    /// </summary>
    private static string? GetInstalledConformanceVersionString()
    {
        try
        {
            var repoRoot = FindRepoRoot();
            var packageJsonPath = Path.Combine(
                repoRoot, "node_modules", "@modelcontextprotocol", "conformance", "package.json");

            if (!File.Exists(packageJsonPath))
            {
                return null;
            }

            using var json = System.Text.Json.JsonDocument.Parse(File.ReadAllText(packageJsonPath));
            if (json.RootElement.TryGetProperty("version", out var versionElement))
            {
                return versionElement.GetString();
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Compares two semantic version strings by precedence, honoring the prerelease component
    /// per the SemVer 2.0.0 rules used here (numeric identifiers compare numerically, a version
    /// with a prerelease has lower precedence than the same version without one, and a shorter
    /// set of prerelease identifiers has lower precedence when all preceding ones are equal).
    /// Build metadata (after '+') is ignored. Returns a negative value when <paramref name="a"/>
    /// precedes <paramref name="b"/>, zero when equal, and a positive value otherwise.
    /// </summary>
    private static int CompareSemVer(string a, string b)
    {
        var (coreA, preA) = SplitSemVer(a);
        var (coreB, preB) = SplitSemVer(b);

        var coreCompare = coreA.CompareTo(coreB);
        if (coreCompare != 0)
        {
            return coreCompare;
        }

        // A version without a prerelease outranks one with a prerelease.
        if (preA.Length == 0 && preB.Length == 0)
        {
            return 0;
        }
        if (preA.Length == 0)
        {
            return 1;
        }
        if (preB.Length == 0)
        {
            return -1;
        }

        var count = Math.Min(preA.Length, preB.Length);
        for (var i = 0; i < count; i++)
        {
            var idA = preA[i];
            var idB = preB[i];
            var numA = int.TryParse(idA, out var na);
            var numB = int.TryParse(idB, out var nb);

            int cmp;
            if (numA && numB)
            {
                cmp = na.CompareTo(nb);
            }
            else if (numA)
            {
                // Numeric identifiers always have lower precedence than alphanumeric ones.
                cmp = -1;
            }
            else if (numB)
            {
                cmp = 1;
            }
            else
            {
                cmp = string.CompareOrdinal(idA, idB);
            }

            if (cmp != 0)
            {
                return cmp;
            }
        }

        return preA.Length.CompareTo(preB.Length);
    }

    /// <summary>
    /// Splits a semver string into its numeric core (major.minor.patch) and its prerelease
    /// identifiers, ignoring any build metadata after '+'. Missing core components default to 0.
    /// </summary>
    private static (Version Core, string[] Prerelease) SplitSemVer(string version)
    {
        var withoutBuild = version.Split(new[] { '+' }, 2)[0];
        var parts = withoutBuild.Split(new[] { '-' }, 2);
        var prerelease = parts.Length > 1 && parts[1].Length > 0
            ? parts[1].Split('.')
            : Array.Empty<string>();

        var coreParts = parts[0].Split('.');
        int Part(int index) => index < coreParts.Length && int.TryParse(coreParts[index], out var v) ? v : 0;
        var core = new Version(Part(0), Part(1), Part(2));

        return (core, prerelease);
    }


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
