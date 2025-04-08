namespace ModelContextProtocol.Protocol.Types;

/// <summary>
/// Response type for the ping request in the Model Context Protocol.
/// </summary>
/// <remarks>
/// <para>
/// The PingResult is returned in response to a ping request, which is used to verify that
/// the connection between client and server is still alive and responsive. Since this is a
/// simple connectivity check, the result is an empty object containing no data.
/// </para>
/// <para>
/// Ping requests can be initiated by either the client or the server to check if the other party
/// is still responsive.
/// </para>
/// <para>
/// <example>
/// Example client usage:
/// <code>
/// // Send a ping to the server to verify it's still responsive
/// await mcpClient.PingAsync(cancellationToken);
/// </code>
/// </example>
/// </para>
/// <para>
/// <see href="https://github.com/modelcontextprotocol/specification/blob/main/schema/">See the MCP schema for details</see>
/// </para>
/// </remarks>
public record PingResult
{
}