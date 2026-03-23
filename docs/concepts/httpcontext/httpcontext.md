---
title: HTTP Context
author: mikekistler
description: How to access the HttpContext in the MCP C# SDK.
uid: httpcontext
---

## HTTP context

When using the Streamable HTTP transport, an MCP server might need to access the underlying <xref:Microsoft.AspNetCore.Http.HttpContext> for a request.
The <xref:Microsoft.AspNetCore.Http.HttpContext> object contains request metadata such as the HTTP headers, authorization context, and the actual path and query string for the request.

To access the <xref:Microsoft.AspNetCore.Http.HttpContext>, the MCP server should add the `IHttpContextAccessor` service to the application service collection (typically in Program.cs).
Then any classes, for example, a class containing MCP tools, should accept an `IHttpContextAccessor` in their constructor and store this for use by its methods.
Methods then use the `HttpContext` property of the accessor to get the current context.

The following code snippet illustrates how to add the `IHttpContextAccessor` service to the application service collection:

[!code-csharp[](samples/Program.cs?name=snippet_AddHttpContextAccessor)]

Any class that needs access to the <xref:Microsoft.AspNetCore.Http.HttpContext> can accept an `IHttpContextAccessor` in its constructor and store it for later use.
Methods of the class can then access the current <xref:Microsoft.AspNetCore.Http.HttpContext> using the stored accessor.

The following code snippet shows the `ContextTools` class accepting an `IHttpContextAccessor` in its primary constructor
and the `GetHttpHeaders` method accessing the current <xref:Microsoft.AspNetCore.Http.HttpContext> to retrieve the HTTP headers from the current request.

[!code-csharp[](samples/Tools/ContextTools.cs?name=snippet_AccessHttpContext)]
