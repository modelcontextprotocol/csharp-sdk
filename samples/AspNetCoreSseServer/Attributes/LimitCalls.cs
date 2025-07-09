using ModelContextProtocol.Core;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace AspNetCoreSseServer.Attributes;

public class LimitCalls(int maxCalls, int order = 0) : ToolFilter(order)
{
    private int _callCount;

    public override ValueTask<CallToolResult>? OnToolCalling(Tool tool, RequestContext<CallToolRequestParams> context)
    {
        _callCount++;
        Console.Out.WriteLine($"Tool: {tool.Name} called {_callCount} time(s)");

        if (_callCount <= maxCalls)
            return null; //do nothing

        return new ValueTask<CallToolResult>(new CallToolResult()
        {
            Content = [new TextContentBlock { Text = $"This tool can only be called {maxCalls} time(s)" }]
        });
    }

    public override bool OnToolListed(Tool tool, RequestContext<ListToolsRequestParams> context)
    {
        var configuration = context.Services?.GetService<IConfiguration>();
        var hide = configuration?["hide-tools-above-limit"] == "True";
        return _callCount <= maxCalls || !hide;
    }
}
