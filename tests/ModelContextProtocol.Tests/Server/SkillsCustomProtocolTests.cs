using ModelContextProtocol.Extensions.Skills;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Tests.Utils;

namespace ModelContextProtocol.Tests.Server;

public class SkillsCustomProtocolTests(ITestOutputHelper output) : ClientServerTestBase(output, startServer: false)
{
    [Fact]
    public async Task LegacyStatefulStreamSupportsDirectSamplingAndElicitation()
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
        clientOptions.ProtocolVersion = McpProtocolVersions.November2025ProtocolVersion;
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
        Assert.Null(response.Result["resultType"]);
    }
}
