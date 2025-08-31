---
title: Elicitation
author: mikekistler
description: Enable interactive AI experiences by requesting user input during tool execution.
uid: elicitation
---

## Elicitation

The **elicitation** feature allows servers to request additional information from users during interactions. This enables more dynamic and interactive AI experiences, making it easier to gather necessary context before executing tasks.

### Server Support for Elicitation

Servers request structured data from users with the [ElicitAsync] extension method on [IMcpServer].
The C# SDK registers an instance of [IMcpServer] with the dependency injection container,
so tools can simply add a parameter of type [IMcpServer] to their method signature to access it.

[ElicitAsync]: https://modelcontextprotocol.github.io/csharp-sdk/api/ModelContextProtocol.Server.McpServerExtensions.html#ModelContextProtocol_Server_McpServerExtensions_ElicitAsync_ModelContextProtocol_Server_IMcpServer_ModelContextProtocol_Protocol_ElicitRequestParams_System_Threading_CancellationToken_
[IMcpServer]: https://modelcontextprotocol.github.io/csharp-sdk/api/ModelContextProtocol.Server.IMcpServer.html

The MCP Server must specify the schema of each input value it is requesting from the user.
Only primitive types (string, number, boolean) are supported for elicitation requests.
The schema may include a description to help the user understand what is being requested.

The server can request a single input or multiple inputs at once.
To help distinguish multiple inputs, each input has a unique name.

The following example demonstrates how a server could request a boolean response from the user.

[!code-csharp[](samples/server/Tools/InteractiveTools.cs?name=snippet_GuessTheNumber)]

### Client Support for Elicitation

Elicitation is an optional feature so clients declare their support for it in their capabilities as part of the `initialize` request. In the MCP C# SDK, this is done by configuring an [ElicitationHandler] in the [McpClientOptions]:

[ElicitationHandler]: https://modelcontextprotocol.github.io/csharp-sdk/api/ModelContextProtocol.Protocol.ElicitationCapability.html#ModelContextProtocol_Protocol_ElicitationCapability_ElicitationHandler
[McpClientOptions]: https://modelcontextprotocol.github.io/csharp-sdk/api/ModelContextProtocol.Client.McpClientOptions.html

[!code-csharp[](samples/client/Program.cs?name=snippet_McpInitialize)]

The ElicitationHandler is an asynchronous method that will be called when the server requests additional information.
The ElicitationHandler must request input from the user and return the data in a format that matches the requested schema.
This will be highly dependent on the client application and how it interacts with the user.

If the user provides the requested information, the ElicitationHandler should return an [ElicitResult] with the action set to "accept" and the content containing the user's input.
If the user does not provide the requested information, the ElicitationHandler should return an [ElicitResult] with the action set to "reject" and no content.

[ElicitResult]: https://modelcontextprotocol.github.io/csharp-sdk/api/ModelContextProtocol.Protocol.ElicitResult.html

Below is an example of how a console application might handle elicitation requests.
Here's an example implementation:

[!code-csharp[](samples/client/Program.cs?name=snippet_ElicitationHandler)]
