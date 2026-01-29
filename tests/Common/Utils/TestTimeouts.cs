namespace ModelContextProtocol.Tests.Utils;

/// <summary>
/// Provides centralized timeout constants for tests to prevent sporadic CI failures
/// due to overloaded machines.
/// </summary>
public static class TestTimeouts
{
    /// <summary>
    /// Default timeout for test operations that may be affected by CI machine load.
    /// Set to 60 seconds to provide sufficient buffer for slow CI environments.
    /// </summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(60);
}
