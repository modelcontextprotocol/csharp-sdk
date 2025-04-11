using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using ModelContextProtocol.Server;
using System.Text.Json;

namespace ModelContextProtocol.Tests.Configuration;

public partial class McpServerScopedTests : ClientServerTestBase
{
    public McpServerScopedTests(ITestOutputHelper testOutputHelper)
        : base(testOutputHelper)
    {
    }

    protected override void ConfigureServices(ServiceCollection services, IMcpServerBuilder mcpServerBuilder)
    {
        mcpServerBuilder.WithTools<EchoTool>(serializerOptions: JsonContext.Default.Options);
        services.AddScoped(_ => new ComplexObject() { Name = "Scoped" });
    }

    [Fact]
    public async Task InjectScopedServiceAsArgument()
    {
        IMcpClient client = await CreateMcpClientForServer();

        var tools = await client.ListToolsAsync(JsonContext.Default.Options, TestContext.Current.CancellationToken);
        var tool = tools.First(t => t.Name == nameof(EchoTool.EchoComplex));
        Assert.DoesNotContain("\"complex\"", JsonSerializer.Serialize(tool.JsonSchema, AIJsonUtilities.DefaultOptions));

        Assert.Contains("\"Scoped\"", JsonSerializer.Serialize(await tool.InvokeAsync(cancellationToken: TestContext.Current.CancellationToken), AIJsonUtilities.DefaultOptions));
    }

    [McpServerToolType]
    public sealed class EchoTool()
    {
        [McpServerTool]
        public static string EchoComplex(ComplexObject complex) => complex.Name!;
    }
}
