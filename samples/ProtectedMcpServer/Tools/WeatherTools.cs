using ModelContextProtocol;
using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Globalization;
using System.Text.Json;

namespace ProtectedMcpServer.Tools;

[McpServerToolType]
public sealed class WeatherTools
{
    private readonly IHttpClientFactory _httpClientFactory;

    public WeatherTools(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    [McpServerTool, Description("Get weather alerts for a US state.")]
    public async Task<string> GetAlerts(
        [Description("The US state to get alerts for. Use the 2 letter abbreviation for the state (e.g. NY).")] string state)
    {
        var client = _httpClientFactory.CreateClient("WeatherApi");
        using var jsonDocument = await client.GetFromJsonAsync<JsonDocument>($"/alerts/active/area/{state}")
            ?? throw new McpException("No JSON returned from alerts endpoint");

        var alerts = jsonDocument.RootElement.GetProperty("features").EnumerateArray();

        if (!alerts.Any())
        {
            return "No active alerts for this state.";
        }

        return string.Join("\n--\n", alerts.Select(alert =>
        {
            JsonElement properties = alert.GetProperty("properties");
            return $"""
                    Event: {properties.GetProperty("event").GetString()}
                    Area: {properties.GetProperty("areaDesc").GetString()}
                    Severity: {properties.GetProperty("severity").GetString()}
                    Description: {properties.GetProperty("description").GetString()}
                    Instruction: {properties.GetProperty("instruction").GetString()}
                    """;
        }));
    }

    [McpServerTool, Description("Get weather forecast for a location.")]
    public async Task<string> GetForecast(
        [Description("Latitude of the location.")] double latitude,
        [Description("Longitude of the location.")] double longitude)
    {
        var client = _httpClientFactory.CreateClient("WeatherApi");
        var pointUrl = string.Create(CultureInfo.InvariantCulture, $"/points/{latitude},{longitude}");

        using var locationDocument = await client.GetFromJsonAsync<JsonDocument>(pointUrl);
        var forecastUrl = locationDocument?.RootElement.GetProperty("properties").GetProperty("forecast").GetString()
            ?? throw new McpException($"No forecast URL provided by {client.BaseAddress}points/{latitude},{longitude}");

        using var forecastDocument = await client.GetFromJsonAsync<JsonDocument>(forecastUrl);
        var periods = forecastDocument?.RootElement.GetProperty("properties").GetProperty("periods").EnumerateArray()
            ?? throw new McpException("No JSON returned from forecast endpoint");

        return string.Join("\n---\n", periods.Select(period => $"""
                {period.GetProperty("name").GetString()}
                Temperature: {period.GetProperty("temperature").GetInt32()}°F
                Wind: {period.GetProperty("windSpeed").GetString()} {period.GetProperty("windDirection").GetString()}
                Forecast: {period.GetProperty("detailedForecast").GetString()}
                """));
    }
}
