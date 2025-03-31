using ModelContextProtocol.Protocol.Transport;
using ModelContextProtocol.Protocol.Types;
using ModelContextProtocol.Server;
using ModelContextProtocol.Tests.Utils;
using Moq;

namespace ModelContextProtocol.Tests.Server;

public class McpServerFactoryTests : LoggedTest
{
    private readonly McpServerOptions _options;

    public McpServerFactoryTests(ITestOutputHelper testOutputHelper)
        : base(testOutputHelper)
    {
        _options = new McpServerOptions
        {
            ServerInfo = new Implementation { Name = "TestServer", Version = "1.0" },
            ProtocolVersion = "1.0",
            InitializationTimeout = TimeSpan.FromSeconds(30)
        };
    }

    [Fact]
    public async Task Create_Should_Initialize_With_Valid_Parameters()
    {
        // Arrange & Act
        await using IMcpServer server = McpServerFactory.Create(Mock.Of<ITransport>(), _options, LoggerFactory);

        // Assert
        Assert.NotNull(server);
    }

    [Fact]
    public async Task Create_Throws_For_Null_ServerTransport()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>("transport", () => McpServerFactory.Create(null!, _options, LoggerFactory));

        await Assert.ThrowsAsync<ArgumentNullException>("serverTransport", () => 
            McpServerFactory.AcceptAsync(null!, _options, LoggerFactory, cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Create_Throws_For_Null_Options()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>("serverOptions", () => McpServerFactory.Create(Mock.Of<ITransport>(), null!, LoggerFactory));

        await Assert.ThrowsAsync<ArgumentNullException>("serverOptions", () => 
            McpServerFactory.AcceptAsync(Mock.Of<IServerTransport>(), null!, LoggerFactory, cancellationToken: TestContext.Current.CancellationToken));
    }
}
