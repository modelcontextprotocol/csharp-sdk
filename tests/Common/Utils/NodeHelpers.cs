using System.Diagnostics;
using System.Runtime.InteropServices;

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
    /// <returns>A configured ProcessStartInfo for running the binary.</returns>
    public static ProcessStartInfo ConformanceTestStartInfo(string arguments)
    {
        EnsureNpmDependenciesInstalled();

        // If MCP_CONFORMANCE_PROTOCOL_VERSION is set, pass it as --spec-version to the runner.
        var protocolVersion = Environment.GetEnvironmentVariable("MCP_CONFORMANCE_PROTOCOL_VERSION");
        if (!string.IsNullOrEmpty(protocolVersion))
        {
            arguments += $" --spec-version {protocolVersion}";
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
    /// Checks whether the SEP-2243 conformance scenarios are available by reading
    /// the conformance package version from the repo's package.json.
    /// The http-standard-headers, http-custom-headers, http-invalid-tool-headers,
    /// http-header-validation, and http-custom-header-server-validation scenarios
    /// require a conformance package version that includes SEP-2243 support.
    /// </summary>
    public static bool HasSep2243Scenarios()
    {
        try
        {
            var repoRoot = FindRepoRoot();
            var packageJsonPath = Path.Combine(repoRoot, "package.json");
            if (!File.Exists(packageJsonPath))
            {
                return false;
            }

            var json = System.Text.Json.JsonDocument.Parse(File.ReadAllText(packageJsonPath));
            if (json.RootElement.TryGetProperty("dependencies", out var deps) &&
                deps.TryGetProperty("@modelcontextprotocol/conformance", out var versionElement))
            {
                var versionStr = versionElement.GetString();
                if (versionStr is not null && Version.TryParse(versionStr, out var version))
                {
                    // SEP-2243 scenarios are expected in conformance package >= 0.2.0
                    return version >= new Version(0, 2, 0);
                }
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

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
