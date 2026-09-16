using System.Text.Json;
using System.Text.Json.Serialization;

namespace ModelContextProtocol.Protocol;

/// <summary>
/// Represents the client's response to an elicitation request.
/// </summary>
public sealed class ElicitResult : Result
{
    /// <summary>
    /// Gets or sets the user action in response to the elicitation.
    /// </summary>
    /// <value>
    /// Defaults to "cancel" if not explicitly set.
    /// </value>
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
    ///     <description>User dismissed without making an explicit choice (default)</description>
    ///   </item>
    /// </list>
    /// </remarks>
    [JsonPropertyName("action")]
    public string Action { get; set; } = "cancel";

    /// <summary>
    /// Gets a value that indicates whether the elicitation was accepted by the user.
    /// </summary>
    /// <remarks>
    /// If <see langword="true"/>, it indicates that the elicitation request completed successfully and the value of <see cref="Content"/> has been populated with a value.
    /// </remarks>
    [JsonIgnore]
    public bool IsAccepted => string.Equals(Action, "accept", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Gets or sets the submitted form data.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This value is typically omitted if the action is "cancel" or "decline".
    /// </para>
    /// <para>
    /// Values in the dictionary should be of types <see cref="JsonValueKind.String"/>, <see cref="JsonValueKind.Number"/>,
    /// <see cref="JsonValueKind.True"/>, <see cref="JsonValueKind.False"/>, or <see cref="JsonValueKind.Array"/> (for multi-select enums).
    /// </para>
    /// </remarks>
    [JsonPropertyName("content")]
    public IDictionary<string, JsonElement>? Content { get; set; }

    /// <summary>
    /// Applies default values from the elicitation request schema to any missing fields in the result content.
    /// </summary>
    internal static ElicitResult WithDefaults(ElicitRequestParams? requestParams, ElicitResult result)
    {
        if (result.IsAccepted && requestParams?.RequestedSchema?.Properties is { } properties)
        {
            Dictionary<string, JsonElement>? newContent = null;

            foreach (KeyValuePair<string, ElicitRequestParams.PrimitiveSchemaDefinition> kvp in properties)
            {
                if ((result.Content is null || !result.Content.ContainsKey(kvp.Key)) &&
                    kvp.Value.GetDefaultAsJsonElement() is { } element)
                {
                    newContent ??= result.Content is not null ?
                        new Dictionary<string, JsonElement>(result.Content) :
                        [];
                    newContent[kvp.Key] = element;
                }
            }

            if (newContent is not null)
            {
                return new ElicitResult { Action = result.Action, Content = newContent, Meta = result.Meta };
            }
        }

        return result;
    }
}

/// <summary>
/// Represents the client's response to an elicitation request, with typed content payload.
/// </summary>
/// <typeparam name="T">The type of the expected content payload.</typeparam>
public sealed class ElicitResult<T> : Result
{
    /// <summary>
    /// Gets or sets the user action in response to the elicitation.
    /// </summary>
    /// <value>
    /// Defaults to "cancel" if not explicitly set.
    /// </value>
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
    ///     <description>User dismissed without making an explicit choice (default)</description>
    ///   </item>
    /// </list>
    /// </remarks>
    public string Action { get; set; } = "cancel";

    /// <summary>
    /// Gets a value that indicates whether the elicitation was accepted by the user.
    /// </summary>
    /// <remarks>
    /// If <see langword="true"/>, it indicates that the elicitation request completed successfully and the value of <see cref="Content"/> has been populated with a value.
    /// </remarks>
    public bool IsAccepted => string.Equals(Action, "accept", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Gets or sets the submitted form data as a typed value.
    /// </summary>
    public T? Content { get; set; }
}
