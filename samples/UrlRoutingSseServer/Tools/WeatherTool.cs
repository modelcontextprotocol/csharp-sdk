using ModelContextProtocol.Server;
using System.ComponentModel;

namespace UrlRoutingSseServer.Tools;

[McpServerToolType]
public sealed class WeatherTool
{
    [McpServerTool, Description("Gets current weather for a location.")]
    [McpServerToolRoute("weather", "utilities")]
    public static string GetWeather([Description("City name")] string city)
    {
        var temps = new[] { 72, 68, 75, 80, 77 };
        var conditions = new[] { "Sunny", "Cloudy", "Rainy", "Partly Cloudy", "Clear" };
        var random = new Random(city.GetHashCode()); // Deterministic based on city

        return $"Weather in {city}: {temps[random.Next(temps.Length)]}°F, {conditions[random.Next(conditions.Length)]}";
    }

    [McpServerTool, Description("Gets 5-day weather forecast.")]
    [McpServerToolRoute("weather")]
    public static string GetForecast([Description("City name")] string city)
    {
        return $"5-day forecast for {city}: Mon 75°F, Tue 73°F, Wed 78°F, Thu 72°F, Fri 76°F";
    }
}
