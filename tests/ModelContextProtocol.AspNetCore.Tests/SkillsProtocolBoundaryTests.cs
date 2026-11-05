using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.AspNetCore.Tests.Utils;
using ModelContextProtocol.Client;
using ModelContextProtocol.Extensions.Skills;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Tests.Utils;

namespace ModelContextProtocol.AspNetCore.Tests;

public class SkillsProtocolBoundaryTests(ITestOutputHelper output) : KestrelInMemoryTest(output)
{
    [Theory]
    [InlineData(false, "2025-11-25", false)]
    [InlineData(false, "2025-11-25", true)]
    [InlineData(true, "2025-11-25", false)]
    [InlineData(true, "2025-11-25", true)]
    [InlineData(true, "2026-07-28", false)]
    [InlineData(true, "2026-07-28", true)]
    public async Task CustomTypedHandlersHaveNoImplicitMrtrOnStatelessHttp(bool stateless, string version, bool sampling)
    {
        Exception? localError = null;
        Builder.Services.AddMcpServer().WithHttpTransport(options => options.SessionMode =
            stateless ? HttpServerSessionMode.Stateless : HttpServerSessionMode.Stateful)
            .WithSkills(options => options.ListSkillsHandler = async (context, cancellationToken) =>
            {
                try
                {
                    if (sampling)
                    {
                        await context.Server.SampleAsync(new CreateMessageRequestParams { Messages = [], MaxTokens = 10 }, cancellationToken);
                    }
                    else
                    {
                        await context.Server.ElicitAsync(new ElicitRequestParams { Message = "Confirm", RequestedSchema = new() }, cancellationToken);
                    }

                    return new ListSkillsResult();
                }
                catch (InvalidOperationException error)
                {
                    localError = error;
                    throw;
                }
            });

        await using var app = Builder.Build();
        app.MapMcp();
        await app.StartAsync(TestContext.Current.CancellationToken);
        await using var transport = new HttpClientTransport(new() { Endpoint = new Uri("http://localhost:5000/") }, HttpClient, LoggerFactory);
        var options = TestMcpClient.CreateOptions();
        options.ProtocolVersion = version;
        options.Handlers.ElicitationHandler = (_, _) => new(new ElicitResult { Action = "accept" });
        options.Handlers.SamplingHandler = (_, _, _) => new(new CreateMessageResult { Model = "test", Content = [] });
        await using var client = await TestMcpClient.CreateAsync(transport, options, LoggerFactory, TestContext.Current.CancellationToken);
        if (stateless)
        {
            var error = await Assert.ThrowsAsync<McpProtocolException>(
                async () => await client.ListSkillsAsync(new ListSkillsRequestParams(), cancellationToken: TestContext.Current.CancellationToken));
            Assert.Equal(McpErrorCode.InternalError, error.ErrorCode);
            Assert.Contains("not supported in stateless mode", Assert.IsType<InvalidOperationException>(localError).Message);
        }
        else
        {
            Assert.Empty(await client.ListSkillsAsync(cancellationToken: TestContext.Current.CancellationToken));
            Assert.Null(localError);
        }
    }
}
