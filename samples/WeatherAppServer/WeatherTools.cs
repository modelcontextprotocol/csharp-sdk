using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Globalization;
using System.Text.Json;

[McpServerToolType]
public sealed class WeatherTools
{
    [McpServerTool(Name = "weather_ui")]
    [McpAppUi(ResourceUri = "ui://weather-app/forecast")]
    [Description("Display an interactive weather forecast UI with city picker.")]
    public static string WeatherUi() => "Showing weather forecast UI.";

    [McpServerTool(Name = "weather_forecast")]
    [McpAppUi(ResourceUri = "ui://weather-app/forecast")]
    [Description("Get weather forecast for a US city. Returns detailed multi-period forecast from the National Weather Service.")]
    public static async Task<CallToolResult> WeatherForecast(
        HttpClient client,
        [Description("US city to get the forecast for")] UsCity cityState)
    {
        var (latitude, longitude) = UsCityData.GetCoordinates(cityState);
        var pointUrl = string.Create(CultureInfo.InvariantCulture, $"/points/{latitude},{longitude}");

        using var locationResponse = await client.GetAsync(pointUrl);
        locationResponse.EnsureSuccessStatusCode();
        using var locationDocument = await JsonDocument.ParseAsync(await locationResponse.Content.ReadAsStreamAsync());

        var forecastUrl = locationDocument.RootElement.GetProperty("properties").GetProperty("forecast").GetString()
            ?? throw new McpException($"No forecast URL provided by weather.gov for {cityState}");

        using var forecastResponse = await client.GetAsync(forecastUrl);
        forecastResponse.EnsureSuccessStatusCode();
        using var forecastDocument = await JsonDocument.ParseAsync(await forecastResponse.Content.ReadAsStreamAsync());

        var periods = forecastDocument.RootElement.GetProperty("properties").GetProperty("periods").EnumerateArray().ToList();

        var structuredPeriods = periods.Select(period => new
        {
            name = period.GetProperty("name").GetString(),
            temperature = period.GetProperty("temperature").GetInt32(),
            temperatureUnit = period.GetProperty("temperatureUnit").GetString(),
            windSpeed = period.GetProperty("windSpeed").GetString(),
            windDirection = period.GetProperty("windDirection").GetString(),
            shortForecast = period.GetProperty("shortForecast").GetString(),
            detailedForecast = period.GetProperty("detailedForecast").GetString(),
            isDaytime = period.GetProperty("isDaytime").GetBoolean()
        }).ToList();

        return new CallToolResult
        {
            Content = [new TextContentBlock { Text = $"Weather forecast for {cityState}." }],
            StructuredContent = JsonSerializer.SerializeToElement(new
            {
                cityState = cityState.ToString(),
                latitude,
                longitude,
                periods = structuredPeriods
            })
        };
    }
}
