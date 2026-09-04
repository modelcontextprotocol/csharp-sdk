#if NET
using ModelContextProtocol.Client;
using System.Diagnostics.CodeAnalysis;

namespace ModelContextProtocol.Tests.Transport;

public class HttpOverStdioClientTransportTests
{
    [Fact]
    public void Constructor_AcceptsExactHttp2StreamableHttpOptions()
    {
        var transport = new HttpOverStdioClientTransport(
            new StdioClientTransportOptions
            {
                Command = "test-server",
                Name = "stdio-name",
            },
            new HttpClientTransportOptions
            {
                Endpoint = new Uri("http://mcp.test/mcp"),
                TransportMode = HttpTransportMode.StreamableHttp,
                Name = "http-name",
            });

        Assert.Equal("http-name", transport.Name);
    }

    [Theory]
    [InlineData(HttpTransportMode.AutoDetect)]
    [InlineData(HttpTransportMode.Sse)]
    public void Constructor_RejectsNonStreamableHttpModes(HttpTransportMode mode)
    {
        var exception = Assert.Throws<ArgumentException>(() => new HttpOverStdioClientTransport(
            new StdioClientTransportOptions { Command = "test-server" },
            new HttpClientTransportOptions
            {
                Endpoint = new Uri("http://mcp.test/mcp"),
                TransportMode = mode,
            }));

        Assert.Equal("httpOptions", exception.ParamName);
    }

    [Fact]
    public void Constructor_RejectsHttpsEndpoint()
    {
        var exception = Assert.Throws<ArgumentException>(() => new HttpOverStdioClientTransport(
            new StdioClientTransportOptions { Command = "test-server" },
            new HttpClientTransportOptions
            {
                Endpoint = new Uri("https://mcp.test/mcp"),
                TransportMode = HttpTransportMode.StreamableHttp,
            }));

        Assert.Equal("httpOptions", exception.ParamName);
    }

    [Fact]
    public void Constructor_RejectsNullOptions()
    {
        var stdioOptions = new StdioClientTransportOptions { Command = "test-server" };
        var httpOptions = new HttpClientTransportOptions
        {
            Endpoint = new Uri("http://mcp.test/mcp"),
            TransportMode = HttpTransportMode.StreamableHttp,
        };

        Assert.Throws<ArgumentNullException>(() => new HttpOverStdioClientTransport(null!, httpOptions));
        Assert.Throws<ArgumentNullException>(() => new HttpOverStdioClientTransport(stdioOptions, null!));
    }

    [Fact]
    public void Transport_IsMarkedExperimental()
    {
        var attribute = Assert.Single(
            typeof(HttpOverStdioClientTransport).GetCustomAttributes(typeof(ExperimentalAttribute), inherit: false)
                .Cast<ExperimentalAttribute>());

        Assert.Equal("MCPEXP001", attribute.DiagnosticId);
    }
}
#endif
