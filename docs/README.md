# Documentation Guidelines

This directory contains the conceptual documentation for the MCP C# SDK, built using [DocFX](https://dotnet.github.io/docfx/).

## Referencing API Types

When referencing types from the MCP C# SDK API in markdown documentation, **always use DocFX xref syntax** instead of direct URLs to API documentation pages. This ensures that:

1. Links remain valid even when API documentation structure changes
2. DocFX can validate that referenced types exist during build
3. Links work correctly in offline documentation
4. DocFX generates proper warnings if referenced types are obsolete or missing

### xref Syntax

Use the `<xref:>` tag to reference types, methods, and properties:

```markdown
<!-- Reference a type -->
<xref:ModelContextProtocol.Client.McpClient>

<!-- Reference a method (use * for overloads) -->
<xref:ModelContextProtocol.McpSession.SendNotificationAsync*>

<!-- Reference a specific property -->
<xref:ModelContextProtocol.Client.McpClient.ServerCapabilities>

<!-- Reference a property on a different type -->
<xref:ModelContextProtocol.Protocol.ServerCapabilities.Logging>

<!-- Reference types from external libraries -->
<xref:System.Progress`1>
<xref:Microsoft.Extensions.Logging.ILogger>
```

### Common Types to Reference

When updating documentation, use these type names instead of obsolete interfaces:

| Obsolete Interface | Current Type | xref |
|--------------------|--------------|------|
| `IMcpEndpoint` | `McpSession` | `<xref:ModelContextProtocol.McpSession>` |
| `IMcpClient` | `McpClient` | `<xref:ModelContextProtocol.Client.McpClient>` |
| `IMcpServer` | `McpServer` | `<xref:ModelContextProtocol.Server.McpServer>` |

Note: `IMcpServerBuilder` is NOT obsolete and should still be referenced.

### Building Documentation

To build the documentation locally and verify xref links:

```bash
make generate-docs
```

This will:
1. Clean previous builds
2. Build the project
3. Generate API documentation
4. Build the DocFX site to `artifacts/_site`

To serve the documentation locally:

```bash
make serve-docs
```

Then navigate to `http://localhost:8080` to view the documentation.

## External Links

For links to external documentation (e.g., Microsoft Learn, MCP specification), regular markdown links are acceptable:

```markdown
[Model Context Protocol](https://modelcontextprotocol.io/)
[ILogger](https://learn.microsoft.com/dotnet/api/microsoft.extensions.logging.ilogger)
```
