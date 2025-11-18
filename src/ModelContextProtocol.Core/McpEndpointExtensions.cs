using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace ModelContextProtocol;

/// <summary>
/// Provides extension methods for interacting with an <see cref="IMcpEndpoint"/>.
/// </summary>
/// <remarks>
/// <para>
/// This class provides strongly-typed methods for working with the Model Context Protocol (MCP) endpoints,
/// simplifying JSON-RPC communication by handling serialization and deserialization of parameters and results.
/// </para>
/// <para>
/// These extension methods are designed to be used with both client (<see cref="IMcpClient"/>) and
/// server (<see cref="IMcpServer"/>) implementations of the <see cref="IMcpEndpoint"/> interface.
/// </para>
/// </remarks>
public static class McpEndpointExtensions
{
}
