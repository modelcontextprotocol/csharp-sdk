using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol.Types;
using ModelContextProtocol.Server;

namespace ModelContextProtocol.Tests.Server;

public class McpServerResourceTests
{
    [Fact]
    public void CanCreateServerWithResourceTemplates()
    {
        var services = new ServiceCollection();

        services.AddMcpServer()
            .WithStdioServerTransport()
            .WithListResourceTemplatesHandler(async (ctx, ct) =>
            {
                return new ListResourceTemplatesResult
                {
                    ResourceTemplates =
                    [
                        new ResourceTemplate { Name = "Static Resource", Description = "A static resource with a numeric ID", UriTemplate = "test://static/resource/{id}" }
                    ]
                };
            })
            .WithReadResourceHandler(async (ctx, ct) =>
            {
                return new ReadResourceResult
                {
                    Contents = [new TextResourceContents
                    {
                        Uri = ctx.Params!.Uri!,
                        Text = "Static Resource",
                        MimeType = "text/plain",
                    }]
                };
            });

        var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IMcpServer>();
    }

    [Fact]
    public void CanCreateServerWithResources()
    {
        var services = new ServiceCollection();

        services.AddMcpServer()
            .WithStdioServerTransport()
            .WithListResourcesHandler(async (ctx, ct) =>
            {
                return new ListResourcesResult
                {
                    Resources =
                    [
                        new Resource { Name = "Static Resource", Description = "A static resource with a numeric ID", Uri = "test://static/resource/foo.txt" }
                    ]
                };
            })
            .WithReadResourceHandler(async (ctx, ct) =>
            {
                return new ReadResourceResult
                {
                    Contents = [new TextResourceContents
                    {
                        Uri = ctx.Params!.Uri!,
                        Text = "Static Resource",
                        MimeType = "text/plain",
                    }]
                };
            });

        var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IMcpServer>();
    }

    [Fact]
    public void CreatingReadHandlerWithNoListHandlerFails()
    {
        var services = new ServiceCollection();
        services.AddMcpServer()
            .WithStdioServerTransport()
            .WithReadResourceHandler(async (ctx, ct) =>
            {
                return new ReadResourceResult
                {
                    Contents = [new TextResourceContents
                    {
                        Uri = ctx.Params!.Uri!,
                        Text = "Static Resource",
                        MimeType = "text/plain",
                    }]
                };
            });
        var sp = services.BuildServiceProvider();
        Assert.Throws<McpException>(() => sp.GetRequiredService<IMcpServer>());
    }
}
