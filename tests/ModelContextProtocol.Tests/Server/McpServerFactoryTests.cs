using ModelContextProtocol.Server;
using ModelContextProtocol.Tests.Utils;

namespace ModelContextProtocol.Tests.Server;

public class McpServerFactoryTests : LoggedTest
{
    private readonly McpServerOptions _options;

    public McpServerFactoryTests(ITestOutputHelper testOutputHelper)
        : base(testOutputHelper)
    {
        _options = new McpServerOptions
        {
            ProtocolVersion = "1.0",
            InitializationTimeout = TimeSpan.FromSeconds(30)
        };
    }

    [Fact]
    public async Task Create_Should_Initialize_With_Valid_Parameters()
    {
        // Arrange & Act
        await using var transport = new TestServerTransport();
        await using IMcpServer server = McpServerFactory.Create(transport, _options, LoggerFactory);

        // Assert
        Assert.NotNull(server);
    }

    [Fact]
    public void Create_Throws_For_Null_ServerTransport()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>("transport", () => McpServerFactory.Create(null!, _options, LoggerFactory));
    }

    [Fact]
    public async Task Create_Throws_For_Null_Options()
    {
        // Arrange, Act & Assert
        await using var transport = new TestServerTransport();
        Assert.Throws<ArgumentNullException>("serverOptions", () => McpServerFactory.Create(transport, null!, LoggerFactory));
    }
}
