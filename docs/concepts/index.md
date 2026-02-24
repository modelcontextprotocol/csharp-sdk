# Conceptual documentation

Welcome to the conceptual documentation for the Model Context Protocol SDK. Here you'll find high-level overviews, explanations, and guides to help you understand how the SDK implements the Model Context Protocol.

## Contents

### Base Protocol

| Title | Description |
| - | - |
| [Capabilities](capabilities/capabilities.md) | Learn how client and server capabilities are negotiated during initialization, including protocol version negotiation. |
| [Transports](transports/transports.md) | Learn how to configure stdio, Streamable HTTP, and SSE transports for client-server communication. |
| [Ping](ping/ping.md) | Learn how to verify connection health using the ping mechanism. |
| [Progress tracking](progress/progress.md) | Learn how to track progress for long-running operations through notification messages. |
| [Cancellation](cancellation/cancellation.md) | Learn how to cancel in-flight MCP requests using cancellation tokens and notifications. |
| [Pagination](pagination/pagination.md) | Learn how to use cursor-based pagination when listing tools, prompts, and resources. |

### Client Features

| Title | Description |
| - | - |
| [Sampling](sampling/sampling.md) | Learn how servers request LLM completions from the client using the sampling feature. |
| [Roots](roots/roots.md) | Learn how clients provide filesystem roots to servers for context-aware operations. |
| [Elicitation](elicitation/elicitation.md) | Learn how to request additional information from users during interactions. |

### Server Features

| Title | Description |
| - | - |
| [Tools](tools/tools.md) | Learn how to implement and consume tools that return text, images, audio, and embedded resources. |
| [Resources](resources/resources.md) | Learn how to expose and consume data through MCP resources, including templates and subscriptions. |
| [Prompts](prompts/prompts.md) | Learn how to implement and consume reusable prompt templates with rich content types. |
| [Completions](completions/completions.md) | Learn how to implement argument auto-completion for prompts and resource templates. |
| [Logging](logging/logging.md) | Learn how to implement logging in MCP servers and how clients can consume log messages. |
| [HTTP Context](httpcontext/httpcontext.md) | Learn how to access the underlying `HttpContext` for a request. |
| [MCP Server Handler Filters](filters.md) | Learn how to add filters to the handler pipeline. Filters let you wrap the original handler with additional functionality. |
