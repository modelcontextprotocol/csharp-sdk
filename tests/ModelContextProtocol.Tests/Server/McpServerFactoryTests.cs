using ModelContextProtocol.Protocol.Transport;
using ModelContextProtocol.Protocol.Types;
using ModelContextProtocol.Server;
using ModelContextProtocol.Tests.Utils;
using Moq;

namespace ModelContextProtocol.Tests.Server;

public class McpServerFactoryTests : LoggedTest
{
    private readonly Mock<IServerTransport> _serverTransport;
    private readonly McpServerOptions _options;

    public McpServerFactoryTests(ITestOutputHelper testOutputHelper)
        : base(testOutputHelper)
    {
        _serverTransport = new Mock<IServerTransport>();
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
        await using IMcpServer server = McpServerFactory.Create(_serverTransport.Object, _options, LoggerFactory);

        // Assert
        Assert.NotNull(server);
    }

    [Fact]
    public void Constructor_Throws_For_Null_ServerTransport()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>("serverTransport", () => McpServerFactory.Create(null!, _options, LoggerFactory));
    }

    [Fact]
    public void Constructor_Throws_For_Null_Options()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>("serverOptions", () => McpServerFactory.Create(_serverTransport.Object, null!, LoggerFactory));
    }
}
