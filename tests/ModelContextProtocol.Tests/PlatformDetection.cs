using System.Runtime.InteropServices;

namespace ModelContextProtocol.Tests;

internal static class PlatformDetection
{
    public static bool IsMonoRuntime { get; } = Type.GetType("Mono.Runtime") is not null;
    public static bool IsWindows { get; } = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
}