using System.ComponentModel;

namespace EverythingServer.Tools;

public class WeatherTool
{
    [Description("Gets the current weather for a location")]
    public static string GetWeather(
        [Description("The city name")] string city,
        [Description("The country code (e.g., US, UK)")] string? country = null)
    {
        var location = country != null ? $"{city}, {country}" : city;
        return $"The weather in {location} is sunny with a temperature of 72°F.";
    }
}
