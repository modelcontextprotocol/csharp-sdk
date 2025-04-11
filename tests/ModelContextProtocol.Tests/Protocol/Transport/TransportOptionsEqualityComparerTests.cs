using ModelContextProtocol.Protocol.Transport;

namespace ModelContextProtocol.Tests.Protocol.Transport;

public class TransportOptionsEqualityComparerTests
{
    [Fact]
    public void Stdio_IdenticalObjects_AreEqual()
    {
        var options1 = new StdioClientTransportOptions
        {
            Command = "cmd",
            Arguments = new List<string> { "arg1", "arg2" },
            EnvironmentVariables = new Dictionary<string, string> { { "KEY", "VALUE" } },
            Name = "Test",
            WorkingDirectory = "/tmp",
            ShutdownTimeout = TimeSpan.FromSeconds(10)
        };
        var options2 = new StdioClientTransportOptions
        {
            Command = "cmd",
            Arguments = new List<string> { "arg1", "arg2" },
            EnvironmentVariables = new Dictionary<string, string> { { "KEY", "VALUE" } },
            Name = "Test",
            WorkingDirectory = "/tmp",
            ShutdownTimeout = TimeSpan.FromSeconds(10)
        };

        Assert.True(StdioClientTransportOptionsEqualityComparer.Default.Equals(options1, options2));
        Assert.Equal(StdioClientTransportOptionsEqualityComparer.Default.GetHashCode(options1),
            StdioClientTransportOptionsEqualityComparer.Default.GetHashCode(options2));
    }

    [Fact]
    public void Stdio_DifferentSimpleProperty_AreNotEqual()
    {
        var options1 = new StdioClientTransportOptions { Command = "cmd1" };
        var options2 = new StdioClientTransportOptions { Command = "cmd2" };

        Assert.False(StdioClientTransportOptionsEqualityComparer.Default.Equals(options1, options2));
    }

    [Fact]
    public void Stdio_DifferentArgumentListContent_AreNotEqual()
    {
        var options1 = new StdioClientTransportOptions { Command = "cmd", Arguments = new List<string> { "arg1" } };
        var options2 = new StdioClientTransportOptions { Command = "cmd", Arguments = new List<string> { "arg2" } };

        Assert.False(StdioClientTransportOptionsEqualityComparer.Default.Equals(options1, options2));
    }

    [Fact]
    public void Stdio_DifferentArgumentListOrder_AreNotEqual()
    {
        var options1 = new StdioClientTransportOptions
            { Command = "cmd", Arguments = new List<string> { "arg1", "arg2" } };
        var options2 = new StdioClientTransportOptions
            { Command = "cmd", Arguments = new List<string> { "arg2", "arg1" } };

        Assert.False(StdioClientTransportOptionsEqualityComparer.Default.Equals(options1, options2));
    }

    [Fact]
    public void Stdio_DifferentEnvironmentVariables_AreNotEqual()
    {
        var options1 = new StdioClientTransportOptions
            { Command = "cmd", EnvironmentVariables = new Dictionary<string, string> { { "K1", "V1" } } };
        var options2 = new StdioClientTransportOptions
            { Command = "cmd", EnvironmentVariables = new Dictionary<string, string> { { "K2", "V2" } } };

        Assert.False(StdioClientTransportOptionsEqualityComparer.Default.Equals(options1, options2));
    }

    [Fact]
    public void Stdio_SameEnvironmentVariablesDifferentOrder_AreEqual()
    {
        var options1 = new StdioClientTransportOptions
        {
            Command = "cmd", EnvironmentVariables = new Dictionary<string, string> { { "K1", "V1" }, { "K2", "V2" } }
        };
        var options2 = new StdioClientTransportOptions
        {
            Command = "cmd", EnvironmentVariables = new Dictionary<string, string> { { "K2", "V2" }, { "K1", "V1" } }
        };

        Assert.True(StdioClientTransportOptionsEqualityComparer.Default.Equals(options1, options2));
        Assert.Equal(StdioClientTransportOptionsEqualityComparer.Default.GetHashCode(options1),
            StdioClientTransportOptionsEqualityComparer.Default.GetHashCode(options2));
    }

    [Fact]
    public void Stdio_NullVsEmptyCollections_AreNotEqual()
    {
        var options1 = new StdioClientTransportOptions
            { Command = "cmd", Arguments = null, EnvironmentVariables = null };
        var options2 = new StdioClientTransportOptions
        {
            Command = "cmd", Arguments = new List<string>(), EnvironmentVariables = new Dictionary<string, string>()
        };

        Assert.False(StdioClientTransportOptionsEqualityComparer.Default.Equals(options1, options2));
    }


    [Fact]
    public void Sse_IdenticalObjects_AreEqual()
    {
        var options1 = new SseClientTransportOptions
        {
            Endpoint = new Uri("https://example.com"),
            AdditionalHeaders = new Dictionary<string, string> { { "Header", "Value" } },
            Name = "SseTest",
            ConnectionTimeout = TimeSpan.FromSeconds(15),
            MaxReconnectAttempts = 5,
            ReconnectDelay = TimeSpan.FromSeconds(2)
        };
        var options2 = new SseClientTransportOptions
        {
            Endpoint = new Uri("https://example.com"),
            AdditionalHeaders = new Dictionary<string, string> { { "Header", "Value" } },
            Name = "SseTest",
            ConnectionTimeout = TimeSpan.FromSeconds(15),
            MaxReconnectAttempts = 5,
            ReconnectDelay = TimeSpan.FromSeconds(2)
        };

        Assert.True(SseClientTransportOptionsEqualityComparer.Default.Equals(options1, options2));
        Assert.Equal(SseClientTransportOptionsEqualityComparer.Default.GetHashCode(options1),
            SseClientTransportOptionsEqualityComparer.Default.GetHashCode(options2));
    }

    [Fact]
    public void Sse_DifferentSimpleProperty_AreNotEqual()
    {
        var options1 = new SseClientTransportOptions { Endpoint = new Uri("https://one.com") };
        var options2 = new SseClientTransportOptions { Endpoint = new Uri("https://two.com") };

        Assert.False(SseClientTransportOptionsEqualityComparer.Default.Equals(options1, options2));
    }

    [Fact]
    public void Sse_DifferentAdditionalHeaders_AreNotEqual()
    {
        var options1 = new SseClientTransportOptions
        {
            Endpoint = new Uri("https://a.com"), AdditionalHeaders = new Dictionary<string, string> { { "H1", "V1" } }
        };
        var options2 = new SseClientTransportOptions
        {
            Endpoint = new Uri("https://a.com"), AdditionalHeaders = new Dictionary<string, string> { { "H2", "V2" } }
        };

        Assert.False(SseClientTransportOptionsEqualityComparer.Default.Equals(options1, options2));
    }

    [Fact]
    public void Sse_SameAdditionalHeadersDifferentOrder_AreEqual()
    {
        var options1 = new SseClientTransportOptions
        {
            Endpoint = new Uri("https://b.com"),
            AdditionalHeaders = new Dictionary<string, string> { { "H1", "V1" }, { "H2", "V2" } }
        };
        var options2 = new SseClientTransportOptions
        {
            Endpoint = new Uri("https://b.com"),
            AdditionalHeaders = new Dictionary<string, string> { { "H2", "V2" }, { "H1", "V1" } }
        };

        Assert.True(SseClientTransportOptionsEqualityComparer.Default.Equals(options1, options2));
        Assert.Equal(SseClientTransportOptionsEqualityComparer.Default.GetHashCode(options1),
            SseClientTransportOptionsEqualityComparer.Default.GetHashCode(options2));
    }

    [Fact]
    public void Sse_NullVsEmptyCollections_AreNotEqual()
    {
        var options1 = new SseClientTransportOptions { Endpoint = new Uri("https://c.com"), AdditionalHeaders = null };
        var options2 = new SseClientTransportOptions
            { Endpoint = new Uri("https://c.com"), AdditionalHeaders = new Dictionary<string, string>() };

        Assert.False(SseClientTransportOptionsEqualityComparer.Default.Equals(options1, options2));
    }
}