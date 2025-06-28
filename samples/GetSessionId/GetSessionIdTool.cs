using ModelContextProtocol.Server;
using System.ComponentModel;

[McpServerToolType]
public class GetSessionIdTool
{
    public GetSessionIdTool()
    {
    }

    [McpServerTool(Name = "get_session"), Description("Returns current session id")]
    public async Task<string?> GetSession(IMcpServer mcpServer)
    {
        return mcpServer.SessionId;
    }
}
