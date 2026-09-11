using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Client;

internal sealed class OllamaProviderArrayRegression
{
    public async Task RunAsync(McpClientTool tool)
    {
        foreach (bool streaming in new[] { false, true })
        {
            await VerifyAsync(tool, streaming, """["one","two"]""", ["one", "two"]);
            await VerifyAsync(tool, streaming, "[]", []);
        }
    }

    private async Task VerifyAsync(McpClientTool tool, bool streaming, string itemsJson, string[] expectedItems)
    {
        using var handler = new ToolCallResponseHandler(itemsJson, streaming);
        using var http = new HttpClient(handler);
        using var provider = new Ollama.OllamaClient(
            httpClient: http,
            baseUri: new Uri("http://127.0.0.1:11434"),
            disposeHttpClient: false);
        using var client = new ObservingFunctionClient(provider)
        {
            MaximumConsecutiveErrorsPerRequest = 0,
        };
        var messages = new[] { new ChatMessage(ChatRole.User, "Join the supplied items.") };
        var options = new ChatOptions { ModelId = "mcp-array-regression", Tools = [tool] };
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var expectedResult = $"Joined: {string.Join(',', expectedItems)}";
        using var json = JsonDocument.Parse(itemsJson);
        var controlResult = await tool.InvokeAsync(new() { ["items"] = json.RootElement.Clone() }, timeout.Token);
        if (controlResult is not TextContent controlText ||
            controlText.Text != expectedResult)
        {
            throw new InvalidOperationException("The already-supported JsonElement array control failed.");
        }

        if (streaming)
        {
            await foreach (var _ in client.GetStreamingResponseAsync(messages, options, timeout.Token))
            {
            }
        }
        else
        {
            await client.GetResponseAsync(messages, options, timeout.Token);
        }

        if (client.InvocationCount != 1 ||
            handler.RequestCount != 1 ||
            client.Items is null ||
            !client.Items.SequenceEqual(expectedItems) ||
            client.Result != expectedResult)
        {
            throw new InvalidOperationException(
                $"Ollama array regression failed: streaming={streaming}, expectedItems={expectedItems.Length}, " +
                $"invocations={client.InvocationCount}, requests={handler.RequestCount}, result={client.Result}.");
        }

        Console.WriteLine($"Ollama provider array passed: streaming={streaming}, count={expectedItems.Length}.");
    }

    private sealed class ObservingFunctionClient(IChatClient provider) : FunctionInvokingChatClient(provider)
    {
        public int InvocationCount { get; private set; }
        public string[]? Items { get; private set; }
        public string? Result { get; private set; }

        protected override async ValueTask<object?> InvokeFunctionAsync(
            FunctionInvocationContext context, CancellationToken cancellationToken)
        {
            InvocationCount++;
            if (!context.Arguments.TryGetValue("items", out var value) ||
                value is not List<object> items ||
                items.Any(item => item is not string) ||
                !ReferenceEquals(value, context.CallContent.Arguments?["items"]))
            {
                throw new InvalidOperationException("The real provider did not supply an unchanged List<object> argument.");
            }

            Items = items.Cast<string>().ToArray();
            try
            {
                var result = await base.InvokeFunctionAsync(context, cancellationToken);
                Result = (result as TextContent)?.Text;
                return result;
            }
            finally
            {
                // The regression ends at MCP invocation, not the provider's later tool-result formatting.
                context.Terminate = true;
            }
        }
    }

    private sealed class ToolCallResponseHandler(string itemsJson, bool streaming) : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Content is null ||
                request.Method != HttpMethod.Post ||
                request.RequestUri?.AbsolutePath != "/api/chat" ||
                ++RequestCount != 1)
            {
                throw new InvalidOperationException("Unexpected provider request.");
            }

            using var document = JsonDocument.Parse(await request.Content.ReadAsStringAsync(cancellationToken));
            if (document.RootElement.GetProperty("model").GetString() != "mcp-array-regression" ||
                document.RootElement.GetProperty("stream").GetBoolean() != streaming)
            {
                throw new InvalidOperationException("The provider request did not match the scenario.");
            }

            var response = """
                {"model":"mcp-array-regression","created_at":"2026-08-28T16:00:00Z","message":{"role":"assistant","content":"","tool_calls":[{"function":{"name":"Join","arguments":{"items":__ITEMS__}}}]},"done":true,"done_reason":"stop","eval_count":1,"prompt_eval_count":1}
                """.Replace("__ITEMS__", itemsJson, StringComparison.Ordinal);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    response + "\n", Encoding.UTF8, streaming ? "application/x-ndjson" : "application/json"),
            };
        }
    }
}
