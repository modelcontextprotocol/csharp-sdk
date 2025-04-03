using ModelContextProtocol.Protocol.Transport;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using System.Net;

namespace ModelContextProtocol.Tests.Configuration;

public class McpServerBuilderExtensionsTransportsTests
{
    [Fact]
    public void WithStdioServerTransport_Sets_Transport()
    {
        var services = new ServiceCollection();
        var builder = new Mock<IMcpServerBuilder>();
        builder.SetupGet(b => b.Services).Returns(services);

        builder.Object.WithStdioServerTransport();

        var transportType = services.FirstOrDefault(s => s.ServiceType == typeof(ITransport));
        Assert.NotNull(transportType);
        Assert.Equal(typeof(StdioServerTransport), transportType.ImplementationType);
    }
    [Fact]
    public void WithTcpServerTransport_Sets_Transport()
    {
        var services = new ServiceCollection();
        var builder = new Mock<IMcpServerBuilder>();
        builder.SetupGet(b => b.Services).Returns(services);

        builder.Object.WithTcpServerTransport();

        var transportType = services.FirstOrDefault(s => s.ServiceType == typeof(ITransport));
        Assert.NotNull(transportType);
        Assert.Equal(typeof(TcpServerTransport), transportType.ImplementationType);
    }
    [Fact]
    public void WithTcpServerTransport_Sets_Transport_Options()
    {
        var services = new ServiceCollection();
        var builder = new Mock<IMcpServerBuilder>();
        builder.SetupGet(b => b.Services).Returns(services);

        builder.Object.WithTcpServerTransport(options =>
        {
            options.Port = 12345;
            options.IpAddress = IPAddress.Parse("127.0.0.1");
        });

        var transportType = services.FirstOrDefault(s => s.ServiceType == typeof(ITransport));
        Assert.NotNull(transportType);
        Assert.Equal(typeof(TcpServerTransport), transportType.ImplementationType);
    }
}
