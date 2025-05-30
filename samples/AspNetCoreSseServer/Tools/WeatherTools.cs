using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using ModelContextProtocol.Server;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Threading;


namespace AspNetCoreSseServer.Tools;

[McpServerToolType]
public sealed class WeatherTools
{
    private readonly IHttpClientFactory _httpClientFactory;

    public WeatherTools(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }
    [McpServerTool(Name = "get_current_weather"), Description("returns the current weather given a town or region name")]
    public async Task<string> Get_Weather(IMcpServer mcpServer, [Description("The location (town or region) name")] string location)
    {
        try
        {
            var client = _httpClientFactory.CreateClient();
            var request = new HttpRequestMessage(HttpMethod.Get, $"https://nominatim.openstreetmap.org/search?format=json&q={location}");
            request.Headers.Add("User-Agent", "Test-MCP-Server");
            var ret = await client.SendAsync(request);
            if (!ret.IsSuccessStatusCode)
            {
                return $"error getting coordinates from location StatusCode: {ret.StatusCode} message: {await ret.Content.ReadAsStringAsync()}";
            }
            var response = ret.Content.ReadAsStreamAsync();
            var locationInfo = JsonNode.Parse(await response) as JsonArray ?? new JsonArray();
            if (locationInfo == null || locationInfo.Count == 0)
            {
                return $"could not parse no result {response} into an json array or no results were found for location {location}";
            }
            request = new HttpRequestMessage(HttpMethod.Get, $"https://api.open-meteo.com/v1/forecast?latitude={locationInfo.First()?["lat"]}&longitude={locationInfo.First()?["lon"]}9&current_weather=true");
            request.Headers.Add("User-Agent", "Test-MCP-Server");
            ret = await client.SendAsync(request);
            if (!ret.IsSuccessStatusCode)
            {
                return $"error getting coordinates from location StatusCode: {ret.StatusCode} message: {await ret.Content.ReadAsStringAsync()}";
            }
            return await ret.Content.ReadAsStringAsync();
        }
        catch (Exception ex)
        {
            return $"general error: {ex.ToString()}";
        }
    }
}




public record PlaceInformation
{
    public int place_id { get; init; }
    public string licence { get; init; } = "";
    public string osm_type { get; init; } = "";
    public int osm_id { get; init; }
    public string lat { get; init; } = "";
    public string lon { get; init; } = "";

    public string type { get; init; } = "";
    public int place_rank { get; init; }
    public double importance { get; init; }
    public string addresstype { get; init; } = "";
    public string name { get; init; } = "";
    public string display_name { get; init; } = "";
    public List<string> boundingbox { get; init; } = new List<string>();
}


