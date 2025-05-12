using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol.Transport;
using ModelContextProtocol.Protocol.Types;
using Microsoft.Extensions.AI;
using Microsoft.AspNetCore.DataProtection;
using AspNetCoreClient;
var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
//This client use AspNetCoreSseServer .. so AspNetCoreSseServer must be runing as well

// THIS IS NEEDED FOR SAMPLING CALLBACKS : START 
var modelName = builder.Configuration["model-name"];
ArgumentException.ThrowIfNullOrWhiteSpace(modelName, "model-name not found in configuration");  
var openAIApiKey = builder.Configuration["open-ai-api-key"]; //PUT IN SECRET FILE OR ENV VARIABLE 
ArgumentException.ThrowIfNullOrWhiteSpace(openAIApiKey, "open-ai-api-key not found in configuration");
var client = new OpenAI.OpenAIClient(openAIApiKey);
var chatClient = client.GetChatClient(modelName);
// ChatClientProxy just for demo purpouses, to intercept call back  
var samplingClient =new ChatClientProxy(chatClient);
builder.Services.AddSingleton(chatClient);
//END

var useStreamableHttp = builder.Configuration["UseStreamableHttp"] ?? "true";
var sse = "";
if (useStreamableHttp != "true")
{
    sse = "/sse";
}
var transport = new SseClientTransport(new SseClientTransportOptions { Endpoint = new Uri($"{builder.Configuration["mcp-server"]}{sse}"), UseStreamableHttp = useStreamableHttp != "true" ? false : true });
// it's important to register using a factory, so I can access ILoggerFactory and pas it to McpClientFactory.CreateAsync
// if not the apparently the mcpclient will recreate a logging setup for any log (not only the mcp client logs) 
// and logs will not go to console but to debug window 
builder.Services.AddSingleton((serviceProvider) =>
{
    var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
    var mcpClient = McpClientFactory.CreateAsync(transport, new McpClientOptions
    {
        Capabilities = new ClientCapabilities
        {
            Sampling = new SamplingCapability() { SamplingHandler = samplingClient.CreateSamplingHandler() }
        }
    }, loggerFactory).Result;
    return mcpClient;
});
builder.Services.AddSingleton<ITemplatesProvider, TemplatesProvider>(); 
var app = builder.Build();


app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();
