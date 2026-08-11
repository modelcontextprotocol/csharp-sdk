# ASP.NET Core MCP server sample

This sample hosts an MCP server with Streamable HTTP and configures OpenTelemetry for MCP operations, ASP.NET Core requests, outgoing HTTP requests, metrics, and structured logs.

## Export to Application Insights

Set the Application Insights connection string and run the sample:

```bash
export APPLICATIONINSIGHTS_CONNECTION_STRING='InstrumentationKey=<your-instrumentation-key>;IngestionEndpoint=https://<region>.in.applicationinsights.azure.com/'
dotnet run --project samples/AspNetCoreMcpServer
```

PowerShell:

```powershell
$env:APPLICATIONINSIGHTS_CONNECTION_STRING = 'InstrumentationKey=<your-instrumentation-key>;IngestionEndpoint=https://<region>.in.applicationinsights.azure.com/'
dotnet run --project samples/AspNetCoreMcpServer
```

The connection string is read through ASP.NET Core configuration. Keep it outside source control and use your deployment platform's secret configuration in production.

When `APPLICATIONINSIGHTS_CONNECTION_STRING` is absent or empty, the sample retains its default OTLP exporter. Configure that path with standard `OTEL_EXPORTER_OTLP_*` environment variables.

## Signals and MCP attributes

Application Insights receives the signals already emitted by the SDK and the configured ASP.NET Core/OpenTelemetry instrumentation:

- server activities appear as requests and client activities appear as dependencies;
- structured logs appear as traces;
- MCP histograms appear as custom metrics;
- HTTP server and client instrumentation provides the surrounding transport spans.

MCP activities can include attributes such as `mcp.method.name`, `mcp.protocol.version`, `mcp.session.id`, `jsonrpc.request.id`, and `gen_ai.tool.name`. The exact attributes depend on the operation and whether sensitive-data telemetry is enabled.

For example, query MCP request and dependency telemetry in Logs:

```kusto
union withsource=TelemetryType requests, dependencies
| extend McpMethod = tostring(customDimensions["mcp.method.name"]),
         ToolName = tostring(customDimensions["gen_ai.tool.name"]),
         McpSession = tostring(customDimensions["mcp.session.id"])
| where isnotempty(McpMethod)
| project timestamp, TelemetryType, name, success, resultCode, McpMethod, ToolName, McpSession
| order by timestamp desc
```

Invalid tool calls, malformed JSON-RPC messages, and other failures appear only when the current SDK or ASP.NET Core instrumentation emits a log or activity for that path. This sample exports existing telemetry; it does not add custom protocol events or per-tool instrumentation.
