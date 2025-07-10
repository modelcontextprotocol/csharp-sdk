using ModelContextProtocol.Core;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace AspNetCoreSseServer.Attributes;

public class LimitCallsAttribute(int maxCalls, int order = 0) : ToolFilter(order)
{
    private int _callCount;

    public override ValueTask<CallToolResult>? OnToolCalling(Tool tool, RequestContext<CallToolRequestParams> context)
    {
        //Thread-safe increment
        var currentCount = Interlocked.Add(ref _callCount, 1);
        
        //Log count
        Console.Out.WriteLine($"Tool: {tool.Name} called {currentCount} time(s)");

        //If under threshold, do nothing
        if (currentCount <= maxCalls)
            return null; //do nothing

        //If above threshold, return error message
        return new ValueTask<CallToolResult>(new CallToolResult
        {
            Content = [new TextContentBlock { Text = $"This tool can only be called {maxCalls} time(s)" }]
        });
    }

    public override bool OnToolListed(Tool tool, RequestContext<ListToolsRequestParams> context)
    {
        //With the provided request context, you can access the dependency injection
        var configuration = context.Services?.GetService<IConfiguration>();
        var hide = configuration?["hide-tools-above-limit"] == "True";
        
        //Prevent the tool being listed (return false)
        //if the hide flag is true and the call count is above the threshold
        return _callCount <= maxCalls || !hide;
    }
}
