using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Net.Http.Headers;
using System.Text.Json;

namespace QuickstartWeatherServer.Tools;

[McpServerToolType]
public static class WeatherTools
{
    [McpServerTool, Description("Get weather alerts for a US state.")]
    public static async Task<string> GetAlerts(
        [Description("The US state to get alerts for.")] string state)
    {
        using HttpClient client = GetWeatherClient();

        var response = await client.GetAsync($"/alerts/active/area/{state}");

        if (!response.IsSuccessStatusCode)
        {
            return "Failed to retrieve alerts.";
        }

        var json = await response.Content.ReadAsStringAsync();
        var jsonElement = JsonSerializer.Deserialize<JsonElement>(json);
        var alerts = jsonElement.GetProperty("features").EnumerateArray();

        if (!alerts.Any())
        {
            return "No active alerts for this state.";
        }

        // Process the alerts and return a formatted string
        var alertMessages = new List<string>();
        foreach (var alert in alerts)
        {
            JsonElement properties = alert.GetProperty("properties");
            alertMessages.Add($"""
                    Event: {properties.GetProperty("event").GetString()}
                    Area: {properties.GetProperty("areaDesc").GetString()}
                    Severity: {properties.GetProperty("severity").GetString()}
                    Description: {properties.GetProperty("description").GetString()}
                    Instruction: {properties.GetProperty("instruction").GetString()}
                    """);
        }
        return string.Join("\n---\n", alertMessages);
    }

    [McpServerTool, Description("Get weather forecast for a location.")]
    public static async Task<string> GetForecast(
        [Description("Latitude of the location.")] double latitude,
        [Description("Longitude of the location.")] double longitude)
    {
        using HttpClient client = GetWeatherClient();
        var response = await client.GetAsync($"/points/{latitude},{longitude}");
        if (!response.IsSuccessStatusCode)
        {
            return "Failed to retrieve forecast.";
        }

        var json = await response.Content.ReadAsStringAsync();
        var jsonElement = JsonSerializer.Deserialize<JsonElement>(json);
        var periods = jsonElement.GetProperty("properties").GetProperty("periods").EnumerateArray();
        // Process the forecast and return a formatted string
        var forecastMessages = new List<string>();
        foreach (var period in periods)
        {
            forecastMessages.Add($"""
                    {period.GetProperty("name").GetString()}
                    Temperature: {period.GetProperty("temperature").GetInt32()}°F
                    Wind: {period.GetProperty("windSpeed").GetString()} {period.GetProperty("windDirection").GetString()}
                    Forecast: {period.GetProperty("detailedForecast").GetString()}
                    """);
        }
        return string.Join("\n---\n", forecastMessages);
    }

    private static HttpClient GetWeatherClient()
    {
        var client = new HttpClient() { BaseAddress = new Uri("https://api.weather.gov") };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("weather-tool", "1.0"));
        return client;
    }
}