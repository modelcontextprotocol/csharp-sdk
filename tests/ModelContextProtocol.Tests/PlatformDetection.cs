using System.Runtime.InteropServices;

namespace ModelContextProtocol.Tests;

internal static class PlatformDetection
{
    public static bool IsMonoRuntime { get; } = Type.GetType("Mono.Runtime") is not null;
    public static bool IsWindows { get; } = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    // On .NET Framework, Exception.Data requires values to be serializable with [Serializable].
    // JsonElement is not marked as serializable, so certain features are not available on that platform.
    public static bool IsNetFramework { get; } =
#if NET
        false;
#else
        true;
#endif
}