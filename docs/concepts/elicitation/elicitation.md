---
title: Elicitation
author: mikekistler
description: Enable interactive AI experiences by requesting user input during tool execution.
uid: elicitation
---

## Elicitation

The **elicitation** feature allows servers to request additional information from users during interactions. This enables more dynamic and interactive AI experiences, making it easier to gather necessary context before executing tasks.

The protocol supports two modes of elicitation:

- **Form (In-Band)**: The server requests structured data (strings, numbers, Booleans, enums) which the client collects via a form interface and returns to the server.
- **URL Mode**: The server provides a URL for the user to visit (for example, for OAuth, payments, or sensitive data entry). The interaction happens outside the MCP client.

### Server Support for Elicitation

Servers request information from users with the <xref:ModelContextProtocol.Server.McpServer.ElicitAsync*> extension method on <xref:ModelContextProtocol.Server.McpServer>.
The C# SDK registers an instance of <xref:ModelContextProtocol.Server.McpServer> with the dependency injection container,
so tools can simply add a parameter of type <xref:ModelContextProtocol.Server.McpServer> to their method signature to access it.

#### Form Mode Elicitation (In-Band)

For form-based elicitation, the MCP Server must specify the schema of each input value it is requesting from the user.
Primitive types (string, number, Boolean) and enum types are supported for elicitation requests.
The schema might include a description to help the user understand what's being requested.

For enum types, the SDK supports several schema formats:

- **UntitledSingleSelectEnumSchema**: A single-select enum where the enum values serve as both the value and display text.
- **TitledSingleSelectEnumSchema**: A single-select enum with separate display titles for each option (using JSON Schema `oneOf` with `const` and `title`).
- **UntitledMultiSelectEnumSchema**: A multi-select enum allowing multiple values to be selected.
- **TitledMultiSelectEnumSchema**: A multi-select enum with display titles for each option.
- **LegacyTitledEnumSchema** (deprecated): The legacy enum schema using `enumNames` for backward compatibility.

#### Default values

Each schema type supports a `Default` property that specifies a pre-populated value for the form field.
Clients should use defaults to pre-fill form fields, making it easier for users to accept common values or see expected input formats.

```csharp
var result = await server.ElicitAsync(new ElicitRequestParams
{
    Message = "Configure your preferences",
    RequestedSchema = new ElicitRequestParams.RequestSchema
    {
        Properties = new Dictionary<string, ElicitRequestParams.PrimitiveSchemaDefinition>
        {
            ["name"] = new ElicitRequestParams.StringSchema
            {
                Description = "Your display name",
                Default = "User"
            },
            ["maxResults"] = new ElicitRequestParams.NumberSchema
            {
                Description = "Maximum number of results",
                Default = 25
            },
            ["enableNotifications"] = new ElicitRequestParams.BooleanSchema
            {
                Description = "Enable push notifications",
                Default = true
            },
            ["theme"] = new ElicitRequestParams.UntitledSingleSelectEnumSchema
            {
                Description = "UI theme",
                Enum = ["light", "dark", "system"],
                Default = "system"
            }
        }
    }
}, cancellationToken);
```

#### Enum schema formats

Enum schemas allow the server to present a set of choices to the user.

- <xref:ModelContextProtocol.Protocol.ElicitRequestParams.UntitledSingleSelectEnumSchema>: Simple single-select where enum values serve as both the value and display text.
- <xref:ModelContextProtocol.Protocol.ElicitRequestParams.TitledSingleSelectEnumSchema>: Single-select with separate display titles for each option using JSON Schema `oneOf` with `const` and `title`.
- <xref:ModelContextProtocol.Protocol.ElicitRequestParams.UntitledMultiSelectEnumSchema>: Multi-select allowing multiple values.
- <xref:ModelContextProtocol.Protocol.ElicitRequestParams.TitledMultiSelectEnumSchema>: Multi-select with display titles.

```csharp
// Titled single-select: display titles differ from values
["priority"] = new ElicitRequestParams.TitledSingleSelectEnumSchema
{
    Description = "Task priority",
    OneOf =
    [
        new() { Const = "p0", Title = "Critical (P0)" },
        new() { Const = "p1", Title = "High (P1)" },
        new() { Const = "p2", Title = "Normal (P2)" },
    ],
    Default = "p2"
},

// Multi-select: user can select multiple values
["tags"] = new ElicitRequestParams.UntitledMultiSelectEnumSchema
{
    Description = "Tags to apply",
    Items = new()
    {
        Enum = ["bug", "feature", "docs", "test"]
    },
    Default = ["bug"]
}
```

The server can request a single input or multiple inputs at once.
To help distinguish multiple inputs, each input has a unique name.

The following example demonstrates how a server could request a Boolean response from the user.

[!code-csharp[](samples/server/Tools/InteractiveTools.cs?name=snippet_GuessTheNumber)]

#### URL Mode Elicitation (Out-of-Band)

For URL mode elicitation, the server provides a URL that the user must visit to complete an action. This is useful for scenarios like OAuth flows, payment processing, or collecting sensitive credentials that should not be exposed to the MCP client.

To request a URL mode interaction, set the `Mode` to "url" and provide a `Url` and `ElicitationId` in the `ElicitRequestParams`.

```csharp
var elicitationId = Guid.NewGuid().ToString();
var result = await server.ElicitAsync(
    new ElicitRequestParams
    {
        Mode = "url",
        ElicitationId = elicitationId,
        Url = $"https://auth.example.com/oauth/authorize?state={elicitationId}",
        Message = "Please authorize access to your account by logging in through your browser."
    },
    cancellationToken);
```

### Client Support for Elicitation

Clients declare their support for elicitation in their capabilities as part of the `initialize` request. Clients can support `Form` (in-band), `Url` (out-of-band), or both.

In the MCP C# SDK, this is done by configuring the capabilities and an <xref:ModelContextProtocol.Client.McpClientHandlers.ElicitationHandler> in the <xref:ModelContextProtocol.Client.McpClientOptions>:

```csharp
var options = new McpClientOptions
{
    Capabilities = new ClientCapabilities
    {
        Elicitation = new ElicitationCapability
        {
            Form = new FormElicitationCapability(),
            Url = new UrlElicitationCapability()
        }
    },
    Handlers = new McpClientHandlers
    {
        ElicitationHandler = HandleElicitationAsync
    }
};
```

The `ElicitationHandler` is an asynchronous method that will be called when the server requests additional information. The handler should check the `Mode` of the request:

- **Form Mode**: Present the form defined by `RequestedSchema` to the user. Return the user's input in the `Content` of the result.
- **URL Mode**: Present the `Message` and `Url` to the user. Ask for consent to open the URL. If the user consents, open the URL and return `Action="accept"`. If the user declines, return `Action="decline"`.

If the user provides the requested information (or consents to URL mode), the ElicitationHandler should return an <xref:ModelContextProtocol.Protocol.ElicitResult> with the action set to "accept".
If the user does not provide the requested information, the ElicitationHandler should return an <xref:ModelContextProtocol.Protocol.ElicitResult> with the action set to "reject" (or "decline" / "cancel").

Here's an example implementation of how a console application might handle elicitation requests:

[!code-csharp[](samples/client/Program.cs?name=snippet_ElicitationHandler)]

### URL Elicitation Required Error

When a tool cannot proceed without first completing a URL-mode elicitation (for example, when third-party OAuth authorization is needed), and calling `ElicitAsync` is not practical (for example in <xref: ModelContextProtocol.AspNetCore.HttpServerTransportOptions.Stateless> is enabled disabling server-to-client requests), the server may throw a <xref:ModelContextProtocol.UrlElicitationRequiredException>. This is a specialized error (JSON-RPC error code `-32042`) that signals to the client that one or more URL-mode elicitations must be completed before the original request can be retried.

#### Throwing UrlElicitationRequiredException on the Server

A server tool can throw `UrlElicitationRequiredException` when it detects that authorization or other out-of-band interaction is required:

```csharp
[McpServerTool, Description("A tool that requires third-party authorization")]
public async Task<string> AccessThirdPartyResource(McpServer server, CancellationToken token)
{
    // Check if we already have valid credentials for this user
    // (In a real app, you'd check stored tokens based on user identity)
    bool hasValidCredentials = false;

    if (!hasValidCredentials)
    {
        // Generate a unique elicitation ID for tracking
        var elicitationId = Guid.NewGuid().ToString();

        // Throw the exception to signal the client needs to complete URL elicitation
        throw new UrlElicitationRequiredException(
            "Authorization is required to access the third-party service.",
            [
                new ElicitRequestParams
                {
                    Mode = "url",
                    ElicitationId = elicitationId,
                    Url = $"https://auth.example.com/connect?elicitationId={elicitationId}",
                    Message = "Please authorize access to your Example Co account."
                }
            ]);
    }

    // Proceed with the authorized operation
    return "Successfully accessed the resource!";
}
```

The exception can include multiple elicitations if the operation requires authorization from multiple services.

#### Catching UrlElicitationRequiredException on the Client

When the client calls a tool and receives a `UrlElicitationRequiredException`, it should:

1. Present each URL elicitation to the user (showing the URL and message)
2. Get user consent before opening each URL
3. Optionally wait for completion notifications from the server
4. Retry the original request after the user completes the out-of-band interactions

```csharp
try
{
    var result = await client.CallToolAsync("AccessThirdPartyResource");
    Console.WriteLine($"Tool succeeded: {result.Content[0]}");
}
catch (UrlElicitationRequiredException ex)
{
    Console.WriteLine($"Authorization required: {ex.Message}");

    // Process each required elicitation
    foreach (var elicitation in ex.Elicitations)
    {
        Console.WriteLine($"\nServer requests URL interaction:");
        Console.WriteLine($"  Message: {elicitation.Message}");
        Console.WriteLine($"  URL: {elicitation.Url}");
        Console.WriteLine($"  Elicitation ID: {elicitation.ElicitationId}");

        // Show security warning and get user consent
        Console.Write("\nDo you want to open this URL? (y/n): ");
        var consent = Console.ReadLine();

        if (consent?.ToLower() == "y")
        {
            // Open the URL in the system browser
            Process.Start(new ProcessStartInfo(elicitation.Url!) { UseShellExecute = true });

            Console.WriteLine("Waiting for you to complete the interaction in your browser...");
            // Optionally listen for notifications/elicitation/complete notification
        }
    }

    // After user completes the out-of-band interaction, retry the tool call
    Console.Write("\nPress Enter to retry the tool call...");
    Console.ReadLine();

    var retryResult = await client.CallToolAsync("AccessThirdPartyResource");
    Console.WriteLine($"Tool succeeded on retry: {retryResult.Content[0]}");
}
```

#### Listening for Elicitation Completion Notifications

Servers can optionally send a `notifications/elicitation/complete` notification when the out-of-band interaction is complete. Clients can register a handler to receive these notifications:

```csharp
await using var completionHandler = client.RegisterNotificationHandler(
    NotificationMethods.ElicitationCompleteNotification,
    async (notification, cancellationToken) =>
    {
        var payload = notification.Params?.Deserialize<ElicitationCompleteNotificationParams>(
            McpJsonUtilities.DefaultOptions);

        if (payload is not null)
        {
            Console.WriteLine($"Elicitation {payload.ElicitationId} completed!");
            // Signal that the client can now retry the original request
        }
    });
```

This pattern is particularly useful for:

- **Third-party OAuth flows**: When the MCP server needs to obtain tokens from external services on behalf of the user
- **Payment processing**: When user confirmation is required through a secure payment interface
- **Sensitive credential collection**: When API keys or other secrets must be entered directly on a trusted server page rather than through the MCP client
