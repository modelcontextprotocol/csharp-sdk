using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Text.Json;

namespace EverythingServer.Tools;

// Demonstrates the SEP-2200 ("Clarify tool result content visibility") recommended pattern:
// return a CallToolResult with a model-friendly Content (prose) AND a machine-friendly
// StructuredContent (strict JSON), advertising the JSON schema via OutputSchemaType.
//
// The default behaviour for [McpServerTool(UseStructuredContent = true)] returning a plain
// object is to JSON-stringify the same payload into both fields. SEP-2200 calls that
// "acceptable but may be suboptimal" — a short prose summary in Content saves tokens and
// is easier for the model to reason about, while StructuredContent stays available for
// programmatic consumers (UI, downstream tools, orchestration logic).
[McpServerToolType]
public class WeatherStructuredTool
{
    public sealed record WeatherReading(string City, int TempF, string Condition, int Humidity);

    [McpServerTool(
        Name = "getWeather",
        UseStructuredContent = true,
        OutputSchemaType = typeof(WeatherReading)),
        Description("Gets the current weather for a city.")]
    public static CallToolResult GetWeather(
        [Description("The city to look up the weather for.")] string city)
    {
        // In a real tool, fetch this from a weather API.
        var reading = new WeatherReading(City: city, TempF: 72, Condition: "sunny", Humidity: 40);

        return new CallToolResult
        {
            // Model-oriented: short, prose-friendly. This is what an LLM reads.
            Content =
            [
                new TextContentBlock
                {
                    Text = $"It's {reading.TempF}°F and {reading.Condition} in {reading.City} (humidity {reading.Humidity}%).",
                },
            ],
            // Machine-oriented: strict JSON for UIs, downstream tools, orchestrators.
            // Validates against the schema generated from typeof(WeatherReading).
            StructuredContent = JsonSerializer.SerializeToElement(reading),
        };
    }
}
