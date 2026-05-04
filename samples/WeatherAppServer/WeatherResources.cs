using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;

[McpServerResourceType]
public sealed class WeatherResources
{
    private static readonly string UiDir = Path.Combine(AppContext.BaseDirectory, "ui");

    [McpServerResource(UriTemplate = "ui://weather-app/forecast", Name = "weather-forecast-ui", MimeType = McpApps.ResourceMimeType)]
    [Description("Interactive weather forecast UI with city picker")]
    public static string GetWeatherForecastUi() => File.ReadAllText(Path.Combine(UiDir, "weather-forecast.html"));

    [McpServerResource(UriTemplate = "data://weather-app/us-cities", Name = "us-cities", MimeType = "application/json")]
    [Description("List of supported US cities for weather forecasts")]
    public static string GetUsCities()
    {
        var options = new JsonSerializerOptions { Converters = { new JsonStringEnumConverter<UsCity>() } };
        var cities = Enum.GetValues<UsCity>().Select(c => JsonSerializer.Serialize(c, options).Trim('"')).Order().ToList();
        return JsonSerializer.Serialize(cities);
    }
}
