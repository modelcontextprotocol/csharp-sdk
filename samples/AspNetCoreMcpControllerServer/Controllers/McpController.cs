using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using ModelContextProtocol.AspNetCore;

namespace AspNetCoreMcpControllerServer.Controllers;

/// <summary>
/// An MVC controller that handles MCP Streamable HTTP transport requests
/// by delegating to a <see cref="RequestDelegate"/> created by
/// <see cref="McpRequestDelegateFactory"/>.
/// </summary>
[ApiController]
[Route("mcp")]
public class McpController : ControllerBase
{
    private static readonly RequestDelegate _mcpHandler = McpRequestDelegateFactory.Create();

    [HttpPost]
    [HttpGet]
    [HttpDelete]
    public Task Handle() => _mcpHandler(HttpContext);
}
