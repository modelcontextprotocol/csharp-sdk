---
title: Sampling
author: jeffhandley
description: How servers request LLM completions from the client using the sampling feature.
uid: sampling
---

## Sampling

MCP [sampling] allows servers to request LLM completions from the client. This enables agentic behaviors where a server-side tool delegates reasoning back to the client's language model — for example, summarizing content, generating text, or making decisions.

[sampling]: https://modelcontextprotocol.io/specification/2025-11-25/client/sampling

> [!NOTE]
> Sampling is a **server-to-client request** — the server sends a request back to the client over an open connection. This requires [stateful mode or stdio](xref:stateless). Sampling is not available in [stateless mode](xref:stateless#stateless-mode-recommended) because stateless servers cannot send requests to clients.

### How sampling works

1. The server calls <xref:ModelContextProtocol.Server.McpServer.SampleAsync*> (or uses the <xref:ModelContextProtocol.Server.McpServer.AsSamplingChatClient*> adapter) during tool execution.
2. The request is sent to the connected client over MCP.
3. The client's <xref:ModelContextProtocol.Client.McpClientHandlers.SamplingHandler> processes the request — typically by forwarding it to an LLM.
4. The client returns the LLM response to the server, which continues tool execution.

### Server: requesting a completion

Inject <xref:ModelContextProtocol.Server.McpServer> into a tool method and use the <xref:ModelContextProtocol.Server.McpServer.AsSamplingChatClient*> extension method to get an <xref:Microsoft.Extensions.AI.IChatClient> that sends requests through the connected client:

```csharp
[McpServerTool(Name = "SummarizeContent"), Description("Summarizes the given text")]
public static async Task<string> Summarize(
    McpServer server,
    [Description("The text to summarize")] string text,
    CancellationToken cancellationToken)
{
    ChatMessage[] messages =
    [
        new(ChatRole.User, "Briefly summarize the following content:"),
        new(ChatRole.User, text),
    ];

    ChatOptions options = new()
    {
        MaxOutputTokens = 256,
        Temperature = 0.3f,
    };

    return $"Summary: {await server.AsSamplingChatClient().GetResponseAsync(messages, options, cancellationToken)}";
}
```

Alternatively, use <xref:ModelContextProtocol.Server.McpServer.SampleAsync*> directly for lower-level control:

```csharp
CreateMessageResult result = await server.SampleAsync(
    new CreateMessageRequestParams
    {
        Messages =
        [
            new SamplingMessage
            {
                Role = Role.User,
                Content = [new TextContentBlock { Text = "What is 2 + 2?" }]
            }
        ],
        MaxTokens = 100,
    },
    cancellationToken);

string response = result.Content.OfType<TextContentBlock>().FirstOrDefault()?.Text ?? string.Empty;
```

### Client: handling sampling requests

Set <xref:ModelContextProtocol.Client.McpClientHandlers.SamplingHandler> when creating the client. This handler is called when a server sends a `sampling/createMessage` request.

#### Using an IChatClient

The simplest approach is to use <xref:ModelContextProtocol.AIContentExtensions.CreateSamplingHandler*> with any <xref:Microsoft.Extensions.AI.IChatClient> implementation:

```csharp
IChatClient chatClient = new OllamaChatClient(new Uri("http://localhost:11434"), "llama3");

McpClientOptions options = new()
{
    Handlers = new()
    {
        SamplingHandler = chatClient.CreateSamplingHandler()
    }
};

await using var client = await McpClient.CreateAsync(transport, options);
```

#### Custom handler

For full control, provide a custom delegate:

```csharp
McpClientOptions options = new()
{
    Handlers = new()
    {
        SamplingHandler = async (request, progress, cancellationToken) =>
        {
            // Forward to your LLM, apply content filtering, etc.
            string prompt = request?.Messages?.LastOrDefault()?.Content
                .OfType<TextContentBlock>().FirstOrDefault()?.Text ?? string.Empty;

            return new CreateMessageResult
            {
                Model = "my-model",
                Role = Role.Assistant,
                Content = [new TextContentBlock { Text = $"Response to: {prompt}" }]
            };
        }
    }
};
```

### Capability negotiation

Sampling requires the client to advertise the `sampling` capability. This is handled automatically — when a <xref:ModelContextProtocol.Client.McpClientHandlers.SamplingHandler> is set, the client includes the sampling capability during initialization. The server can check whether the client supports sampling before calling <xref:ModelContextProtocol.Server.McpServer.SampleAsync*>; if sampling is not supported, the method throws <xref:System.InvalidOperationException>.

### Multi Round-Trip Requests (MRTR)

[MRTR](xref:mrtr) is the SEP-2322 mechanism for server-driven input requests, finalized in protocol revision `2026-07-28`. In that revision, the server-to-client `sampling/createMessage` request method is removed; the recommended way to ask the client to sample from a server handler is to throw <xref:ModelContextProtocol.Protocol.InputRequiredException> and let the SDK emit an <xref:ModelContextProtocol.Protocol.InputRequiredResult> on the wire.

> [!IMPORTANT]
> `SampleAsync` and `AsSamplingChatClient` throw `InvalidOperationException("Sampling is not supported in stateless mode.")` whenever the server is running stateless — including Streamable HTTP requests served under `2026-07-28` with `Stateless = true`. Stdio servers and initialize-handshake stateful Streamable HTTP sessions continue to work via the initialize-era server-to-client `sampling/createMessage` request flow; an HTTP server set to `Stateless = false` refuses `2026-07-28` so dual-path clients can fall back before using that flow. For code that needs to run on stateless servers — including `2026-07-28` Streamable HTTP — throw `InputRequiredException` from your handler instead. It works under both protocols and both session modes.

For example:

```csharp
[McpServerTool, Description("Tool that samples via MRTR")]
public static string SampleWithMrtr(
    McpServer server,
    RequestContext<CallToolRequestParams> context)
{
    // On retry, process the client's sampling response
    if (context.Params!.InputResponses?.TryGetValue("llm_call", out var response) is true)
    {
        var text = response.Deserialize(InputResponse.CreateMessageResultJsonTypeInfo)?.Content
            .OfType<TextContentBlock>().FirstOrDefault()?.Text;
        return $"LLM said: {text}";
    }

    if (!server.IsMrtrSupported)
    {
        return "This tool requires MRTR support (2026-07-28, or a stateful current-protocol session).";
    }

    // First call — request LLM completion from the client
    throw new InputRequiredException(
        inputRequests: new Dictionary<string, InputRequest>
        {
            ["llm_call"] = InputRequest.ForSampling(new CreateMessageRequestParams
            {
                Messages =
                [
                    new SamplingMessage
                    {
                        Role = Role.User,
                        Content = [new TextContentBlock { Text = "Summarize the data" }]
                    }
                ],
                MaxTokens = 256
            })
        },
        requestState: "awaiting-sample");
}
```

> [!TIP]
> See [Multi Round-Trip Requests (MRTR)](xref:mrtr) for the full protocol details, including load shedding, multiple round trips, and the compatibility matrix.
