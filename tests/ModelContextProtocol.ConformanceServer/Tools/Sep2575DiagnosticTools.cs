using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.ComponentModel;

namespace ConformanceServer.Tools;

/// <summary>
/// Diagnostic tools exercised by the SEP-2575 <c>server-stateless</c> conformance scenario. Each
/// tool exists so the harness can observe a framework behavior (capability enforcement, response
/// stream discipline, per-request log gating) that plain application tools never trigger.
/// </summary>
[McpServerToolType]
public class Sep2575DiagnosticTools
{
    /// <summary>
    /// Requires the client to have declared the <c>sampling</c> capability in the per-request
    /// <c>_meta/io.modelcontextprotocol/clientCapabilities</c>. Used to verify the server rejects
    /// undeclared-capability calls with MissingRequiredClientCapabilityError (-32021).
    /// </summary>
    [McpServerTool(Name = "test_missing_capability")]
    [Description("Requires the sampling client capability; used to verify MissingRequiredClientCapabilityError (-32021) (SEP-2575)")]
    public static string MissingCapability(RequestContext<CallToolRequestParams> context)
    {
        if (context.Server.ClientCapabilities?.Sampling is not null)
        {
            return "Client declared the sampling capability; tool executed.";
        }

        throw new MissingRequiredClientCapabilityException(
            new ClientCapabilities { Sampling = new() },
            "sampling capability required but not declared by client");
    }

    /// <summary>
    /// Returns a plain result. Per SEP-2575 the response stream must carry no independent top-level
    /// JSON-RPC requests; a plain response trivially satisfies this. The scenario declares no
    /// elicitation capability, so this tool must not initiate an elicitation.
    /// </summary>
    [McpServerTool(Name = "test_streaming_elicitation")]
    [Description("Streams only result frames; used to verify response streams carry no independent JSON-RPC requests (SEP-2575)")]
    public static string StreamingElicitation()
    {
        return "stream observed: result frames only, no top-level requests";
    }

    /// <summary>
    /// Attempts to emit a log message through the client-logger pipeline, which gates on the
    /// per-request <c>_meta/io.modelcontextprotocol/logLevel</c>. When the client did not opt in,
    /// no notifications/message may be sent for the request.
    /// </summary>
    [McpServerTool(Name = "test_logging_tool")]
    [Description("Attempts to emit a log message; the framework must drop it when the client did not set _meta.../logLevel (SEP-2575)")]
    public static string LoggingTool(RequestContext<CallToolRequestParams> context)
    {
#pragma warning disable MCP9004 // AsClientLoggerProvider is deprecated with the legacy logging/setLevel flow but remains the gated client-log pipeline.
        ILogger logger = context.Server.AsClientLoggerProvider().CreateLogger(nameof(Sep2575DiagnosticTools));
#pragma warning restore MCP9004
        logger.LogInformation("test_logging_tool executed");
        return "Log attempted; framework gates on _meta.../logLevel.";
    }
}
