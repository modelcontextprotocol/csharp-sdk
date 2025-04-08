using ModelContextProtocol.Protocol.Messages;
using System.Text.Json.Serialization;

namespace ModelContextProtocol.Protocol.Types;

/// <summary>
/// Metadata related to the request that provides additional protocol-level information.
/// This class contains properties that are used by the Model Context Protocol
/// for features like progress tracking and other protocol-specific capabilities.
/// </summary>
/// <example>
/// <code>
/// // Create request parameters with progress tracking
/// var requestParams = new CallToolRequestParams
/// {
///     Name = "myTool",
///     Meta = new RequestParamsMetadata
///     {
///         ProgressToken = new ProgressToken("abc123"),
///     }
/// };
/// 
/// // Send the request with progress tracking enabled
/// var result = await client.SendRequestAsync(RequestMethods.ToolsCall, requestParams);
/// 
/// // The server can then use this token to send progress notifications
/// // that will be correlated with this specific request
/// </code>
/// </example>
public class RequestParamsMetadata
{
    /// <summary>
    /// If specified, the caller is requesting out-of-band progress notifications for this request
    /// (as represented by notifications/progress). The value of this parameter is an opaque token 
    /// that will be attached to any subsequent notifications. The receiver is not obligated to 
    /// provide these notifications.
    /// </summary>
    /// <example>
    /// <code>
    /// // Create a progress token for tracking a long-running operation
    /// var progressToken = new ProgressToken("op123");
    /// 
    /// // Attach the token to request metadata
    /// var requestParams = new CallToolRequestParams
    /// {
    ///     Meta = new RequestParamsMetadata
    ///     {
    ///         ProgressToken = progressToken
    ///     }
    /// };
    /// 
    /// // The server can now send progress updates using this token
    /// </code>
    /// </example>
    /// <remarks>
    /// Progress tokens can be created from either string or numeric identifiers and allow 
    /// correlating asynchronous progress notifications with the original request.
    /// </remarks>
    [JsonPropertyName("progressToken")]
    public ProgressToken? ProgressToken { get; set; } = default!;
}
