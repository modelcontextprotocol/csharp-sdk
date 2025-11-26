using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

var endpoint = Environment.GetEnvironmentVariable("ENDPOINT") ?? "http://localhost:3001";

var clientTransport = new HttpClientTransport(new()
{
    Endpoint = new Uri(endpoint),
    TransportMode = HttpTransportMode.StreamableHttp,
});

// <snippet_McpInitialize>
McpClientOptions options = new()
{
    ClientInfo = new()
    {
        Name = "ElicitationClient",
        Version = "1.0.0"
    },
    Handlers = new()
    {
        ElicitationHandler = HandleElicitationAsync
    }
};

await using var mcpClient = await McpClient.CreateAsync(clientTransport, options);
// </snippet_McpInitialize>

var tools = await mcpClient.ListToolsAsync();
foreach (var tool in tools)
{
    Console.WriteLine($"Connected to server with tools: {tool.Name}");
}

Console.WriteLine($"Calling tool: {tools.First().Name}");

var result = await mcpClient.CallToolAsync(toolName: tools.First().Name);

foreach (var block in result.Content)
{
    if (block is TextContentBlock textBlock)
    {
        Console.WriteLine(textBlock.Text);
    }
    else
    {
        Console.WriteLine($"Received unexpected result content of type {block.GetType()}");
    }
}

// <snippet_ElicitationHandler>
async ValueTask<ElicitResult> HandleElicitationAsync(ElicitRequestParams? requestParams, CancellationToken token)
{
    // Bail out if the requestParams is null or if the requested schema has no properties
    if (requestParams is null || requestParams.RequestedSchema?.Properties is null)
    {
        return new ElicitResult();
    }

    // Process the elicitation request
    if (requestParams.Message is not null)
    {
        Console.WriteLine(requestParams.Message);
    }

    var content = new Dictionary<string, JsonElement>();

    // Loop through requestParams.requestSchema.Properties dictionary requesting values for each property
    foreach (var property in requestParams.RequestedSchema.Properties)
    {
        if (property.Value is ElicitRequestParams.BooleanSchema booleanSchema)
        {
            Console.Write($"{booleanSchema.Description}: ");
            var clientInput = Console.ReadLine();
            bool parsedBool;

            // Try standard boolean parsing first
            if (bool.TryParse(clientInput, out parsedBool))
            {
                content[property.Key] = JsonElement.Parse(JsonSerializer.Serialize(parsedBool));
            }
            // Also accept "yes"/"no" as valid boolean inputs
            else if (string.Equals(clientInput?.Trim(), "yes", StringComparison.OrdinalIgnoreCase))
            {
                content[property.Key] = JsonElement.Parse(JsonSerializer.Serialize(true));
            }
            else if (string.Equals(clientInput?.Trim(), "no", StringComparison.OrdinalIgnoreCase))
            {
                content[property.Key] = JsonElement.Parse(JsonSerializer.Serialize(false));
            }
        }
        else if (property.Value is ElicitRequestParams.NumberSchema numberSchema)
        {
            Console.Write($"{numberSchema.Description}: ");
            var clientInput = Console.ReadLine();
            double parsedNumber;
            if (double.TryParse(clientInput, out parsedNumber))
            {
                content[property.Key] = JsonElement.Parse(JsonSerializer.Serialize(parsedNumber));
            }
        }
        else if (property.Value is ElicitRequestParams.StringSchema stringSchema)
        {
            Console.Write($"{stringSchema.Description}: ");
            var clientInput = Console.ReadLine();
            content[property.Key] = JsonElement.Parse(JsonSerializer.Serialize(clientInput));
        }
    }

    // Return the user's input
    return new ElicitResult
    {
        Action = "accept",
        Content = content
    };
}
// </snippet_ElicitationHandler>
