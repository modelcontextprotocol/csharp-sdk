using ModelContextProtocol;
using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Globalization;
using System.Text.Json;

namespace AspNetCoreMcpServer.Tools;

[McpServerToolType]
public sealed class WeatherTools
{
    private readonly IHttpClientFactory _httpClientFactory;

    public WeatherTools(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    [McpServerTool, Description("Get weather alerts for a US state.")]
    [McpMeta("category", "weather")]
    [McpMeta("dataSource", "weather.gov")]
    public async Task<string> GetAlerts(
        [Description("The US state to get alerts for. Use the 2 letter abbreviation for the state (e.g. NY).")] string state)
    {
        var client = _httpClientFactory.CreateClient("WeatherApi");
        using var responseStream = await client.GetStreamAsync($"/alerts/active/area/{state}");
        using var jsonDocument = await JsonDocument.ParseAsync(responseStream)
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
    [McpMeta("category", "weather")]
    [McpMeta("recommendedModel", "gpt-4")]
    public async Task<string> GetForecast(
        [Description("Latitude of the location.")] double latitude,
        [Description("Longitude of the location.")] double longitude)
    {
        var client = _httpClientFactory.CreateClient("WeatherApi");
        var pointUrl = string.Create(CultureInfo.InvariantCulture, $"/points/{latitude},{longitude}");

        using var locationResponseStream = await client.GetStreamAsync(pointUrl);
        using var locationDocument = await JsonDocument.ParseAsync(locationResponseStream);
        var forecastUrl = locationDocument?.RootElement.GetProperty("properties").GetProperty("forecast").GetString()
            ?? throw new McpException($"No forecast URL provided by {client.BaseAddress}points/{latitude},{longitude}");

        using var forecastResponseStream = await client.GetStreamAsync(forecastUrl);
        using var forecastDocument = await JsonDocument.ParseAsync(forecastResponseStream);
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
