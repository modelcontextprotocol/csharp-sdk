using ModelContextProtocol.Extensions.Skills;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Tests.Utils;

namespace ModelContextProtocol.Tests.Server;

public class SkillsCustomProtocolTests(ITestOutputHelper output) : ClientServerTestBase(output, startServer: false)
{
    [Theory]
    [InlineData("2025-11-25")]
    [InlineData("2026-07-28")]
    public async Task StatefulStreamCallbacksUseDirectLegacyRequests_NotImplicitMrtr(string version)
    {
        McpServerBuilder.WithSkills(options => options.ListSkillsHandler = async (context, cancellationToken) =>
        {
            await context.Server.SampleAsync(new CreateMessageRequestParams { Messages = [], MaxTokens = 10 }, cancellationToken);
            await context.Server.ElicitAsync(new ElicitRequestParams { Message = "Confirm", RequestedSchema = new() }, cancellationToken);
            return new ListSkillsResult();
        });

        StartServer();
        int samples = 0, elicitations = 0;
        var clientOptions = TestMcpClient.CreateOptions();
        clientOptions.ProtocolVersion = version;
        clientOptions.Handlers.SamplingHandler = (_, _, _) =>
        {
            samples++;
            return new(new CreateMessageResult { Model = "test", Content = [] });
        };

        clientOptions.Handlers.ElicitationHandler = (_, _) =>
        {
            elicitations++;
            return new(new ElicitResult { Action = "accept" });
        };

        await using var client = await CreateMcpClientForServer(clientOptions);
        var response = await client.SendRequestAsync(new JsonRpcRequest { Method = SkillsProtocol.MethodSkillsList },
            TestContext.Current.CancellationToken);
        Assert.Equal(1, samples);
        Assert.Equal(1, elicitations);
        Assert.Null(response.Result!["inputRequests"]);
        Assert.Equal(version == McpProtocolVersions.July2026ProtocolVersion ? "complete" : null,
            response.Result["resultType"]?.GetValue<string>());
    }
}
