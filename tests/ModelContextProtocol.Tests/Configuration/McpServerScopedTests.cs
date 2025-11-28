using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using ModelContextProtocol.Server;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ModelContextProtocol.Tests.Configuration;

public partial class McpServerScopedTests : ClientServerTestBase
{
    public McpServerScopedTests(ITestOutputHelper testOutputHelper)
        : base(testOutputHelper)
    {
    }

    protected override void ConfigureServices(ServiceCollection services, IMcpServerBuilder mcpServerBuilder)
    {
        mcpServerBuilder.WithTools<EchoTool>(serializerOptions: McpServerScopedTestsJsonContext.Default.Options);
        services.AddScoped(_ => new ComplexObject { Name = "Scoped" });
    }

    [Fact]
    public async Task InjectScopedServiceAsArgument()
    {
        await using McpClient client = await CreateMcpClientForServer();

        var tools = await client.ListToolsAsync(new RequestOptions { JsonSerializerOptions = McpServerScopedTestsJsonContext.Default.Options }, TestContext.Current.CancellationToken);
        var tool = tools.First(t => t.Name == "echo_complex");
        Assert.DoesNotContain("\"complex\"", JsonSerializer.Serialize(tool.JsonSchema, McpJsonUtilities.DefaultOptions));

        int startingConstructed = ComplexObject.Constructed;
        int startingDisposed = ComplexObject.Disposed;

        for (int i = 1; i <= 10; i++)
        {
            Assert.Contains("\"Scoped\"", JsonSerializer.Serialize(await tool.InvokeAsync(cancellationToken: TestContext.Current.CancellationToken), McpJsonUtilities.DefaultOptions));

            Assert.Equal(startingConstructed + i, ComplexObject.Constructed);
            Assert.Equal(startingDisposed + i, ComplexObject.Disposed);
        }
    }

    [McpServerToolType]
    public sealed class EchoTool()
    {
        [McpServerTool]
        public static string EchoComplex(ComplexObject complex) => complex.Name!;
    }

    public class ComplexObject : IAsyncDisposable
    {
        public static int Constructed;
        public static int Disposed;

        public ComplexObject()
        {
            Interlocked.Increment(ref Constructed);
        }

        public ValueTask DisposeAsync()
        {
            Interlocked.Increment(ref Disposed);
            return default;
        }

        public string? Name { get; set; }
        public int Age { get; set; }
    }

    [JsonSerializable(typeof(string))]
    [JsonSerializable(typeof(ComplexObject))]
    [JsonSerializable(typeof(JsonElement))]
    partial class McpServerScopedTestsJsonContext : JsonSerializerContext;
}
