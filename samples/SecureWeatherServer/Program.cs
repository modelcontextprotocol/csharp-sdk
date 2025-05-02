using SecureWeatherServer.Tools;
using System.Net.Http.Headers;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddMcpServer()
    .WithHttpTransport()
    .WithTools<WeatherTools>()
    .WithAuthorization(metadata =>
    {
        metadata.AuthorizationServers.Add(new Uri("https://auth.example.com"));
        metadata.ScopesSupported.AddRange(["weather.read", "weather.write"]);
        metadata.ResourceDocumentation = new Uri("https://docs.example.com/api/weather");
    });

builder.Services.AddSingleton(_ =>
{
    var client = new HttpClient() { BaseAddress = new Uri("https://api.weather.gov") };
    client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("weather-tool", "1.0"));
    return client;
});

var app = builder.Build();

app.UseCors(policy => policy
    .AllowAnyOrigin()
    .AllowAnyMethod()
    .AllowAnyHeader());

app.UseAuthentication();
app.UseAuthorization();

app.Run();
