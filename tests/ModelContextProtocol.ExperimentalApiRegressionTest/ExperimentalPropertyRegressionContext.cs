using System.Text.Json.Serialization;
using ModelContextProtocol.Protocol;

namespace ModelContextProtocol.ExperimentalApiRegressionTest;

/// <summary>
/// This file validates that the System.Text.Json source generator does not produce
/// MCPEXP001 diagnostics for MCP protocol types with experimental properties.
/// </summary>
[JsonSerializable(typeof(Tool))]
[JsonSerializable(typeof(ServerCapabilities))]
[JsonSerializable(typeof(ClientCapabilities))]
[JsonSerializable(typeof(CallToolResult))]
[JsonSerializable(typeof(CallToolRequestParams))]
[JsonSerializable(typeof(CreateMessageRequestParams))]
[JsonSerializable(typeof(ElicitRequestParams))]
internal partial class ExperimentalPropertyRegressionContext : JsonSerializerContext;
