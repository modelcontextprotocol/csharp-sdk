---
title: Elicitation
author: mikekistler
description: Enable interactive AI experiences by requesting user input during tool execution.
uid: elicitation
---

## Elicitation

The **elicitation** feature allows servers to request additional information from users during interactions. This enables more dynamic and interactive AI experiences, making it easier to gather necessary context before executing tasks.

### Server Support for Elicitation

Servers request structured data from users with the <xref:ModelContextProtocol.Server.McpServer.ElicitAsync*> extension method on <xref:ModelContextProtocol.Server.McpServer>.
The C# SDK registers an instance of <xref:ModelContextProtocol.Server.McpServer> with the dependency injection container,
so tools can simply add a parameter of type <xref:ModelContextProtocol.Server.McpServer> to their method signature to access it.

The MCP Server must specify the schema of each input value it is requesting from the user.
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

### Client Support for Elicitation

Elicitation is an optional feature so clients declare their support for it in their capabilities as part of the `initialize` request. In the MCP C# SDK, this is done by configuring an <xref:ModelContextProtocol.Client.McpClientHandlers.ElicitationHandler> in the <xref:ModelContextProtocol.Client.McpClientOptions>:

[!code-csharp[](samples/client/Program.cs?name=snippet_McpInitialize)]

The ElicitationHandler is an asynchronous method that will be called when the server requests additional information.
The ElicitationHandler must request input from the user and return the data in a format that matches the requested schema.
This will be highly dependent on the client application and how it interacts with the user.

If the user provides the requested information, the ElicitationHandler should return an <xref:ModelContextProtocol.Protocol.ElicitResult> with the action set to "accept" and the content containing the user's input.
If the user does not provide the requested information, the ElicitationHandler should return an [<xref:ModelContextProtocol.Protocol.ElicitResult> with the action set to "reject" and no content.

Below is an example of how a console application might handle elicitation requests.
Here's an example implementation:

[!code-csharp[](samples/client/Program.cs?name=snippet_ElicitationHandler)]
