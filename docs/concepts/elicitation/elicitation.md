---
title: Elicitation
author: mikekistler
description: Enable interactive AI experiences by requesting user input during tool execution.
uid: elicitation
---

## Elicitation

The **elicitation** feature allows servers to request additional information from users during interactions. This enables more dynamic and interactive AI experiences, making it easier to gather necessary context before executing tasks.

The protocol supports two modes of elicitation:
- **Form (In-Band)**: The server requests structured data (strings, numbers, booleans, enums) which the client collects via a form interface and returns to the server.
- **URL Mode**: The server provides a URL for the user to visit (e.g., for OAuth, payments, or sensitive data entry). The interaction happens outside the MCP client.

### Server Support for Elicitation

Servers request information from users with the <xref:ModelContextProtocol.Server.McpServer.ElicitAsync*> extension method on <xref:ModelContextProtocol.Server.McpServer>.
The C# SDK registers an instance of <xref:ModelContextProtocol.Server.McpServer> with the dependency injection container,
so tools can simply add a parameter of type <xref:ModelContextProtocol.Server.McpServer> to their method signature to access it.

#### Form Mode Elicitation (In-Band)

For form-based elicitation, the MCP Server must specify the schema of each input value it is requesting from the user.
Primitive types (string, number, boolean) and enum types are supported for elicitation requests.
The schema may include a description to help the user understand what is being requested.

For enum types, the SDK supports several schema formats:
- **UntitledSingleSelectEnumSchema**: A single-select enum where the enum values serve as both the value and display text
- **TitledSingleSelectEnumSchema**: A single-select enum with separate display titles for each option (using JSON Schema `oneOf` with `const` and `title`)
- **UntitledMultiSelectEnumSchema**: A multi-select enum allowing multiple values to be selected
- **TitledMultiSelectEnumSchema**: A multi-select enum with display titles for each option
- **LegacyTitledEnumSchema** (deprecated): The legacy enum schema using `enumNames` for backward compatibility

The server can request a single input or multiple inputs at once.
To help distinguish multiple inputs, each input has a unique name.

The following example demonstrates how a server could request a boolean response from the user.

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

Elicitation is an optional feature so clients declare their support for it in their capabilities as part of the `initialize` request. Clients can support `Form` (in-band), `Url` (out-of-band), or both.

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

Below is an example of how a console application might handle elicitation requests.
Here's an example implementation:

[!code-csharp[](samples/client/Program.cs?name=snippet_ElicitationHandler)]
