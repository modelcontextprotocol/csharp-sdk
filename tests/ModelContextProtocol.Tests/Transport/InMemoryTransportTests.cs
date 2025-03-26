using Microsoft.Extensions.DependencyInjection;

using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol.Messages;
using ModelContextProtocol.Protocol.Transport;
using ModelContextProtocol.Server;
using ModelContextProtocol.Tests.Configuration;
using ModelContextProtocol.Tests.Utils;

using System.Text.Json;

namespace ModelContextProtocol.Tests.Transport;

public class InMemoryTransportTests(ITestOutputHelper testOutputHelper) : LoggedTest(testOutputHelper)
{

    [Fact]
    public async Task SendMessageAsync_Throws_Exception_If_Not_Connected()
    {
        var transport = new InMemoryTransport("test", LoggerFactory);
        var serverTransport = transport.ServerTransport;
        var clientTransport = transport.ClientTransport;

        var message = new JsonRpcRequest { Method = "test" };

        await Assert.ThrowsAsync<McpTransportException>(() => serverTransport.SendMessageAsync(message, TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<McpTransportException>(() => clientTransport.SendMessageAsync(message, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DisposeAsync_Should_Dispose_Resources()
    {
        var transport = new InMemoryTransport("test", LoggerFactory);
        var serverTransport = transport.ServerTransport;
        var clientTransport = transport.ClientTransport;

        await serverTransport.DisposeAsync();
        await clientTransport.DisposeAsync();

        Assert.False(serverTransport.IsConnected);
        Assert.False(clientTransport.IsConnected);
    }

    [Fact]
    public async Task TransportPair_Should_Create_Valid_Transports()
    {
        var transport = new InMemoryTransport("test", LoggerFactory);
        var serverTransport = transport.ServerTransport;
        var clientTransport = transport.ClientTransport;

        Assert.NotNull(clientTransport);
        Assert.NotNull(serverTransport);
        Assert.False(clientTransport.IsConnected);
        Assert.False(serverTransport.IsConnected);

        await clientTransport.DisposeAsync();
        await serverTransport.DisposeAsync();
    }


    [Fact]
    public async Task Message_Should_Flow_From_Client_To_Server()
    {
        var transport = new InMemoryTransport("test", LoggerFactory);
        var serverTransport = transport.ServerTransport;
        var clientTransport = transport.ClientTransport;

        await clientTransport.ConnectAsync(TestContext.Current.CancellationToken);

        var message = new JsonRpcRequest
        {
            Method = "test",
            Id = RequestId.FromNumber(123),
            Params = new Dictionary<string, object> { ["text"] = "Hello, World!" }
        };


        await clientTransport.SendMessageAsync(message, TestContext.Current.CancellationToken);
        await Task.Delay(2, TestContext.Current.CancellationToken);


        Assert.True(serverTransport.MessageReader.TryRead(out var receivedMessage));
        Assert.NotNull(receivedMessage);
        Assert.IsType<JsonRpcRequest>(receivedMessage);

        var request = (JsonRpcRequest)receivedMessage;
        Assert.Equal(123, request.Id.AsNumber);
        Assert.Equal("test", request.Method);

        var requestParams = (Dictionary<string, object>)request.Params!;
        Assert.Equal("Hello, World!", requestParams["text"]);

        // Cleanup
        await clientTransport.DisposeAsync();
        await serverTransport.DisposeAsync();
    }

    [Fact]
    public async Task Message_Should_Flow_From_Server_To_Client()
    {
        var transport = new InMemoryTransport("test", LoggerFactory);
        var serverTransport = transport.ServerTransport;
        var clientTransport = transport.ClientTransport;

        await clientTransport.ConnectAsync(TestContext.Current.CancellationToken);

        var message = new JsonRpcResponse
        {
            Id = RequestId.FromNumber(456),
            Result = new Dictionary<string, object> { ["text"] = "Response from server" }
        };


        await serverTransport.SendMessageAsync(message, TestContext.Current.CancellationToken);
        await Task.Delay(2, TestContext.Current.CancellationToken);

        Assert.True(clientTransport.MessageReader.TryRead(out var receivedMessage));
        Assert.NotNull(receivedMessage);
        Assert.IsType<JsonRpcResponse>(receivedMessage);

        var response = (JsonRpcResponse)receivedMessage;
        Assert.Equal(456, response.Id.AsNumber);

        var responseResult = (Dictionary<string, object>)response.Result!;
        Assert.Equal("Response from server", responseResult["text"]);

        // Cleanup
        await clientTransport.DisposeAsync();
        await serverTransport.DisposeAsync();
    }

    [Fact]
    public async Task Can_List_Registered_Tools()
    {
        ServiceCollection sc = new();
        var builder = sc.AddMcpServer().WithTools<McpServerBuilderExtensionsToolsTests.EchoTool>().WithInMemoryServerTransport();
        var server = sc.BuildServiceProvider().GetRequiredService<IMcpServer>();
        await server.StartAsync(TestContext.Current.CancellationToken);

        IMcpClient client = await server.GetInMemoryClientAsync(TestContext.Current.CancellationToken);


        var tools = await client.ListToolsAsync(TestContext.Current.CancellationToken);
        Assert.Equal(11, tools.Count);

        McpClientTool echoTool = tools.First(t => t.Name == "Echo");
        Assert.Equal("Echo", echoTool.Name);
        Assert.Equal("Echoes the input back to the client.", echoTool.Description);
        Assert.Equal("object", echoTool.JsonSchema.GetProperty("type").GetString());
        Assert.Equal(JsonValueKind.Object, echoTool.JsonSchema.GetProperty("properties").GetProperty("message").ValueKind);
        Assert.Equal("the echoes message", echoTool.JsonSchema.GetProperty("properties").GetProperty("message").GetProperty("description").GetString());
        Assert.Equal(1, echoTool.JsonSchema.GetProperty("required").GetArrayLength());

        McpClientTool doubleEchoTool = tools.First(t => t.Name == "double_echo");
        Assert.Equal("double_echo", doubleEchoTool.Name);
        Assert.Equal("Echoes the input back to the client.", doubleEchoTool.Description);
    }
}
