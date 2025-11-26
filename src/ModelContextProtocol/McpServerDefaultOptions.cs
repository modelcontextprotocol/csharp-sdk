using Microsoft.Extensions.AI;
using System.Text.Json;

namespace ModelContextProtocol.Server;

/// <summary>
/// Holds default options for MCP server primitives (tools, prompts, resources).
/// This is separate from McpServerOptions to avoid circular dependencies during service resolution.
/// </summary>
internal sealed class McpServerDefaultOptions
{
    /// <summary>
    /// Gets or sets the default JSON serializer options.
    /// </summary>
    public JsonSerializerOptions? JsonSerializerOptions { get; set; }

    /// <summary>
    /// Gets or sets the default JSON schema creation options.
    /// </summary>
    public AIJsonSchemaCreateOptions? SchemaCreateOptions { get; set; }
}
