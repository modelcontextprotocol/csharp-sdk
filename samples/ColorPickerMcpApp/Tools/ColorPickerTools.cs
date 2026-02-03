using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Text.Json.Nodes;

namespace ColorPickerMcpApp.Tools;

/// <summary>
/// Tools for the color picker MCP App.
/// </summary>
[McpServerToolType]
public class ColorPickerTools
{
    /// <summary>
    /// Request the user to pick a color using an interactive color picker UI.
    /// This tool should be called by the LLM when it needs the user to select a color.
    /// The result will be returned after the user picks a color and submits their selection.
    /// </summary>
    /// <param name="prompt">Optional prompt text to display to the user explaining what the color is for.</param>
    /// <param name="initialColor">Optional initial color to pre-select in the picker (hex format like #ff0000).</param>
    /// <returns>A message indicating the color picker has been displayed to the user.</returns>
    [McpServerTool]
    [Description("Request the user to pick a color using an interactive UI. Use this when you need the user to select a color for any purpose.")]
    // SEP-1865: Associate this tool with the color picker UI resource
    [McpMeta("ui", JsonValue = """{"resourceUri": "ui://color-picker/picker"}""")]
    public static CallToolResult RequestColorPick(
        [Description("Optional prompt explaining what the color is for")] string? prompt = null,
        [Description("Optional initial color in hex format (e.g., #ff0000)")] string? initialColor = null)
    {
        // The tool input parameters will be passed to the UI via ui/notifications/tool-input
        // The actual color selection happens in the UI
        return new CallToolResult
        {
            Content =
            [
                new TextContentBlock { Text = $"Color picker displayed. {(prompt != null ? $"Prompt: {prompt}" : "")}" }
            ],
            // Use structuredContent to pass data to the UI
            StructuredContent = new JsonObject
            {
                ["status"] = "pending",
                ["prompt"] = prompt,
                ["initialColor"] = initialColor ?? "#3b82f6"
            }
        };
    }

    /// <summary>
    /// Called by the UI when the user submits their color selection.
    /// This tool is marked with app-only visibility so the LLM won't see it directly.
    /// </summary>
    /// <param name="color">The selected color in hex format.</param>
    /// <returns>The selected color result.</returns>
    [McpServerTool]
    [Description("Submit the selected color from the color picker UI.")]
    // SEP-1865: This tool is app-only (visibility: ["app"]) - not visible to the model
    [McpMeta("ui", JsonValue = """{"resourceUri": "ui://color-picker/picker", "visibility": ["app"]}""")]
    public static CallToolResult SubmitColor(
        [Description("The selected color in hex format (e.g., #ff5733)")] string color)
    {
        // Validate hex color format
        if (string.IsNullOrEmpty(color) || !color.StartsWith('#') || (color.Length != 4 && color.Length != 7))
        {
            return new CallToolResult
            {
                Content =
                [
                    new TextContentBlock { Text = $"Invalid color format: {color}. Expected hex format like #fff or #ffffff." }
                ],
                IsError = true
            };
        }

        // Return the selected color to the model
        return new CallToolResult
        {
            Content =
            [
                new TextContentBlock { Text = $"User selected color: {color}" }
            ],
            StructuredContent = new JsonObject
            {
                ["status"] = "completed",
                ["color"] = color
            }
        };
    }
}
