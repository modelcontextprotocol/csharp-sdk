using ModelContextProtocol.Protocol;

namespace ModelContextProtocol.Tests.Client;

public class McpRequestHeadersTests
{
    [Fact]
    public void McpHttpHeaders_HasCorrectValues()
    {
        Assert.Equal("Mcp-Session-Id", McpHttpHeaders.SessionId);
        Assert.Equal("MCP-Protocol-Version", McpHttpHeaders.ProtocolVersion);
        Assert.Equal("Last-Event-ID", McpHttpHeaders.LastEventId);
        Assert.Equal("Mcp-Method", McpHttpHeaders.Method);
        Assert.Equal("Mcp-Name", McpHttpHeaders.Name);
        Assert.Equal("Mcp-Param-", McpHttpHeaders.ParamPrefix);
    }

    [Fact]
    public void McpErrorCode_HeaderMismatch_HasCorrectValue()
    {
        Assert.Equal(-32001, (int)McpErrorCode.HeaderMismatch);
    }
}
