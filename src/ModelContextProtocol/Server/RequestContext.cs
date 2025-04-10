using Microsoft.Extensions.DependencyInjection;

namespace ModelContextProtocol.Server;

/// <summary>
/// Provides a context container that provides access to both the server instance and the client request parameters.
/// </summary>
/// <typeparam name="TParams">Type of the request parameters specific to each MCP operation.</typeparam>
/// <remarks>
/// The <see cref="RequestContext{TParams}"/> encapsulates all contextual information for handling an MCP request.
/// This type is typically received as a parameter in handler delegates registered with <see cref="IMcpServerBuilder"/>,
/// and may be injected as parameters into <see cref="McpServerTool"/>s.
/// </remarks>
public record RequestContext<TParams>(IMcpServer Server, TParams? Params);