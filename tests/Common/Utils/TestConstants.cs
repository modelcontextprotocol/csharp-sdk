namespace ModelContextProtocol.Tests.Utils;

/// <summary>
/// Provides centralized constants for tests
/// </summary>
public static class TestConstants
{
    /// <summary>
    /// Default timeout for test operations that may be affected by CI machine load.
    /// Set to 60 seconds to provide sufficient buffer for slow CI environments.
    /// </summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(60);
}
