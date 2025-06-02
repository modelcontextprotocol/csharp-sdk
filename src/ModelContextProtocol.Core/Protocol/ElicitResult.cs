using System.Text.Json;
using System.Text.Json.Serialization;

namespace ModelContextProtocol.Protocol;

/// <summary>
/// Represents the client's response to an elicitation request.
/// </summary>
public class ElicitResult
{
    /// <summary>
    /// Gets or sets the user action in response to the elicitation.
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    ///   <item>
    ///     <term>"accept"</term>
    ///     <description>User submitted the form/confirmed the action</description>
    ///   </item>
    ///   <item>
    ///     <term>"decline"</term>
    ///     <description>User explicitly declined the action</description>
    ///   </item>
    ///   <item>
    ///     <term>"cancel"</term>
    ///     <description>User dismissed without making an explicit choice</description>
    ///   </item>
    /// </list>
    /// </remarks>
    [JsonPropertyName("action")]
    public string Action { get; set; } = "cancel";

    /// <summary>
    /// Gets or sets the submitted form data.
    /// </summary>
    /// <remarks>
    /// This is typically omitted if the action is "cancel" or "decline".
    /// </remarks>
    [JsonPropertyName("content")]
    public JsonElement? Content { get; set; }
}