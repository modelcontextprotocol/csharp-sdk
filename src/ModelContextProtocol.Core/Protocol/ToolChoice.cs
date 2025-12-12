using System.Text.Json.Serialization;

namespace ModelContextProtocol.Protocol;

/// <summary>
/// Controls tool selection behavior for sampling requests.
/// </summary>
public sealed class ToolChoice
{
    /// <summary>
    /// Gets or sets the mode that controls which tools the model can call.
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item><term>"auto"</term><description>Model decides whether to call tools (default)</description></item>
    /// <item><term>"required"</term><description>Model must call at least one tool</description></item>
    /// <item><term>"none"</term><description>Model must not call any tools</description></item>
    /// </list>
    /// </remarks>
    [JsonPropertyName("mode")]
    public string? Mode { get; set; }

    /// <summary>The mode value "auto".</summary>
    internal const string ModeAuto = "auto";

    /// <summary>The mode value "required".</summary>
    internal const string ModeRequired = "required";

    /// <summary>The mode value "none".</summary>
    internal const string ModeNone = "none";
}

