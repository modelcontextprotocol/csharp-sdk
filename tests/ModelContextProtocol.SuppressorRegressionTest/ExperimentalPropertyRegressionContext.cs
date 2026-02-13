using System.Text.Json.Serialization;
using ModelContextProtocol.Protocol;

namespace ModelContextProtocol.SuppressorRegressionTest;

/// <summary>
/// This file validates that the MCPEXP001 diagnostic suppressor works correctly.
/// By including MCP protocol types that have experimental backing fields in a
/// <see cref="JsonSerializerContext"/>, we verify that the source generator does
/// not produce unsuppressed MCPEXP001 diagnostics. If the suppressor is removed
/// or broken, this project will fail to build.
/// </summary>
[JsonSerializable(typeof(Tool))]
[JsonSerializable(typeof(ServerCapabilities))]
[JsonSerializable(typeof(ClientCapabilities))]
[JsonSerializable(typeof(CallToolResult))]
[JsonSerializable(typeof(CallToolRequestParams))]
[JsonSerializable(typeof(CreateMessageRequestParams))]
[JsonSerializable(typeof(ElicitRequestParams))]
internal partial class ExperimentalPropertyRegressionContext : JsonSerializerContext;
