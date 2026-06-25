using ModelContextProtocol.Protocol;
using System.Text.Json;

namespace ModelContextProtocol.Tests.Protocol;

/// <summary>
/// Serialization tests for the error data payloads introduced by the 2026-07-28 protocol revision (SEP-2575).
/// </summary>
public static class July2026ProtocolErrorDataTests
{
    [Fact]
    public static void UnsupportedProtocolVersionErrorData_SerializationRoundTrip_PreservesAllProperties()
    {
        var original = new UnsupportedProtocolVersionErrorData
        {
            Supported = new List<string> { "2024-11-05", "2025-03-26", "2025-06-18", "2025-11-25" },
            Requested = "2026-07-28",
        };

        var json = JsonSerializer.Serialize(original, McpJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<UnsupportedProtocolVersionErrorData>(json, McpJsonUtilities.DefaultOptions);

        Assert.NotNull(deserialized);
        Assert.Equal(4, deserialized.Supported.Count);
        Assert.Contains("2025-11-25", deserialized.Supported);
        Assert.Equal("2026-07-28", deserialized.Requested);
    }

    [Fact]
    public static void MissingRequiredClientCapabilityErrorData_SerializationRoundTrip_PreservesAllProperties()
    {
        var original = new MissingRequiredClientCapabilityErrorData
        {
            RequiredCapabilities = new ClientCapabilities
            {
                Sampling = new SamplingCapability(),
            },
        };

        var json = JsonSerializer.Serialize(original, McpJsonUtilities.DefaultOptions);
        var deserialized = JsonSerializer.Deserialize<MissingRequiredClientCapabilityErrorData>(json, McpJsonUtilities.DefaultOptions);

        Assert.NotNull(deserialized);
        Assert.NotNull(deserialized.RequiredCapabilities.Sampling);
    }

    [Fact]
    public static void UnsupportedProtocolVersionException_ExposesRequestedAndSupported()
    {
        var ex = new UnsupportedProtocolVersionException("2099-12-31", ["2025-11-25", "2025-06-18"]);

        Assert.Equal(McpErrorCode.UnsupportedProtocolVersion, ex.ErrorCode);
        Assert.Equal("2099-12-31", ex.Requested);
        Assert.Equal(2, ex.Supported.Count);
        Assert.Contains("2025-11-25", ex.Supported);
    }

    [Fact]
    public static void MissingRequiredClientCapabilityException_ExposesRequiredCapabilities()
    {
        var caps = new ClientCapabilities { Roots = new RootsCapability() };
        var ex = new MissingRequiredClientCapabilityException(caps);

        Assert.Equal(McpErrorCode.MissingRequiredClientCapability, ex.ErrorCode);
        Assert.Same(caps, ex.RequiredCapabilities);
    }
}
