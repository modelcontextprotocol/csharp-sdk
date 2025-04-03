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
            .WithListResourceTemplatesHandler((ctx, ct) =>
            {
                return Task.FromResult(new ListResourceTemplatesResult
                {
                    ResourceTemplates =
                    [
                        new ResourceTemplate { Name = "Static Resource", Description = "A static resource with a numeric ID", UriTemplate = "test://static/resource/{id}" }
                    ]
                });
            })
            .WithReadResourceHandler((ctx, ct) =>
            {
                return Task.FromResult(new ReadResourceResult
                {
                    Contents = [new TextResourceContents
                    {
                        Uri = ctx.Params!.Uri!,
                        Text = "Static Resource",
                        MimeType = "text/plain",
                    }]
                });
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
            .WithListResourcesHandler((ctx, ct) =>
            {
                return Task.FromResult(new ListResourcesResult
                {
                    Resources =
                    [
                        new Resource { Name = "Static Resource", Description = "A static resource with a numeric ID", Uri = "test://static/resource/foo.txt" }
                    ]
                });
            })
            .WithReadResourceHandler((ctx, ct) =>
            {
                return Task.FromResult(new ReadResourceResult
                {
                    Contents = [new TextResourceContents
                    {
                        Uri = ctx.Params!.Uri!,
                        Text = "Static Resource",
                        MimeType = "text/plain",
                    }]
                });
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
            .WithReadResourceHandler((ctx, ct) =>
            {
                return Task.FromResult(new ReadResourceResult
                {
                    Contents = [new TextResourceContents
                    {
                        Uri = ctx.Params!.Uri!,
                        Text = "Static Resource",
                        MimeType = "text/plain",
                    }]
                });
            });
        var sp = services.BuildServiceProvider();
        Assert.Throws<McpServerException>(() => sp.GetRequiredService<IMcpServer>());
    }
}
