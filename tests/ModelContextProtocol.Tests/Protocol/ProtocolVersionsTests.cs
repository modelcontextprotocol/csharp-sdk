using ModelContextProtocol.Protocol;

namespace ModelContextProtocol.Tests.Protocol;

public class ProtocolVersionsTests
{
    [Fact]
    public void SupportedVersions_IsNotEmpty()
    {
        Assert.NotEmpty(ProtocolVersions.SupportedVersions);
    }

    [Fact]
    public void SupportedVersions_ContainsExpectedVersions()
    {
        Assert.Contains("2024-11-05", ProtocolVersions.SupportedVersions);
        Assert.Contains("2025-03-26", ProtocolVersions.SupportedVersions);
        Assert.Contains("2025-06-18", ProtocolVersions.SupportedVersions);
    }

    [Fact]
    public void SupportedVersions_ReturnsSameInstance()
    {
        Assert.Same(ProtocolVersions.SupportedVersions, ProtocolVersions.SupportedVersions);
    }
}
