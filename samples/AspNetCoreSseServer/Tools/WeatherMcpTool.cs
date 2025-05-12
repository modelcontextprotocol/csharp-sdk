using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using ModelContextProtocol.Server;
using System;
using System.ComponentModel;
using System.Net.Http;
using System.Text.Json;
using System.Threading;


namespace AspNetCoreSseServer.Tools
{
    [McpServerToolType]
    public sealed class WeatherMcpTool 
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _configuration;

        public WeatherMcpTool(IHttpClientFactory httpClientFactory, IConfiguration configuration)
        {
            _httpClientFactory = httpClientFactory;
            _configuration = configuration;
        }
        [McpServerTool(Name ="get_current_weather"), Description("returns the current weather given a town or region name")]
        public async Task<string> Get_Weather(IMcpServer mcpServer, [Description("The location (town or region) name. IMPORTANT : Assistant must ask the user a value for location. If not provided in the conversation, Assistant must not not make up one")]  string location, [Description("If this paramter is set to true, the weather data require post processing to adjust temperature. DO NOT ASSUME FALSE. ask the user if wants postprocessing or not")] bool requiresPostProcessing) {
            try
            {
                var client = _httpClientFactory.CreateClient();
                var weatherkey = _configuration["weatherApiKey"];
                ArgumentException.ThrowIfNullOrWhiteSpace(weatherkey, "weatherApiKey not found in configuration");
                var ret = await client.GetAsync($"http://api.weatherstack.com/current?access_key={weatherkey}&query={location}&units=m");
                if (!ret.IsSuccessStatusCode)
                {
                    throw new Exception($"{ret.StatusCode} + {await ret.Content.ReadAsStringAsync()}");
                }
                var response = await ret.Content.ReadAsStringAsync();
                if (requiresPostProcessing)
                {
                    if (mcpServer.ClientCapabilities?.Sampling is not null)
                    {

                        ChatMessage[] messages =
                        [
                            new(ChatRole.User, "return a modified json where the temperature is raised by 100. JSON RETURN THE MODIFIED JSON, without any preamble"),
                            new(ChatRole.User, response),
                        ];

                        ChatOptions options = new()
                        {
                            MaxOutputTokens = 1000,
                            Temperature = 0.3f,
                        };
                        var sampledResponse = await mcpServer.AsSamplingChatClient().GetResponseAsync(messages, options);
                        return sampledResponse.Messages[0].Text;
                    }
                    else
                    {
                        return "postProcessing has been requested but the client does not support sampling";
                    }
                }
                else
                {
                    return response;    
                }
            }
            catch (Exception ex)
            {
                return $"Error: {ex.ToString()}";

            }
           
        }
    }


}
