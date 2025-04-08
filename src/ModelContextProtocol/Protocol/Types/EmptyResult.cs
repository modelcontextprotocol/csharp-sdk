using System.Text.Json.Serialization;

namespace ModelContextProtocol.Protocol.Types;

/// <summary>
/// Represents an empty result object for operations that need to indicate successful completion 
/// but don't need to return any specific data.
/// </summary>
/// <remarks>
/// This class is commonly used as a return type for handlers in the Model Context Protocol such as:
/// <list type="bullet">
///   <item><description>SubscribeToResourcesHandler</description></item>
///   <item><description>UnsubscribeFromResourcesHandler</description></item>
///   <item><description>SetLoggingLevelHandler</description></item>
/// </list>
/// 
/// <example>
/// Example usage in a handler:
/// <code>
/// .WithSubscribeToResourcesHandler((ctx, ct) =>
/// {
///     // Process subscription request
///     var uri = ctx.Params?.Uri;
///     if (uri is not null)
///     {
///         subscriptions.Add(uri);
///     }
///     return Task.FromResult(new EmptyResult());
/// })
/// </code>
/// </example>
/// 
/// <see href="https://github.com/modelcontextprotocol/specification/blob/main/schema/">See the MCP specification for details</see>
/// </remarks>
public class EmptyResult
{
    [JsonIgnore]
    internal static Task<EmptyResult> CompletedTask { get; } = Task.FromResult(new EmptyResult());
}