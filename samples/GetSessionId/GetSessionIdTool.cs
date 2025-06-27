using ModelContextProtocol.Server;
using System.ComponentModel;

[McpServerToolType]
public class GetSessionIdTool
{
    private readonly IMcpServer _mcpServer;
    public GetSessionIdTool(IMcpServer mcpServer)
    {
        _mcpServer = mcpServer;
    }

    [McpServerTool(Name = "get_session"), Description("Returns current session id")]
    public async Task<string?> GetSession()
    {
        return _mcpServer.SessionId;
    }
}
