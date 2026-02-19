---
title: Experimental APIs
author: MackinnonBuck
description: Working with experimental APIs in the MCP C# SDK
uid: experimental
---

The Model Context Protocol C# SDK uses the [`[Experimental]`](https://learn.microsoft.com/dotnet/api/system.diagnostics.codeanalysis.experimentalattribute) attribute to mark APIs that are still in development and may change without notice. For more details on the SDK's versioning policy around experimental APIs, see the [Versioning](versioning.md) documentation.

## Suppressing experimental diagnostics

When you use an experimental API, the compiler produces a diagnostic (e.g., `MCPEXP001`) to ensure you're aware the API may change. If you want to use the API, suppress the diagnostic in one of these ways:

### Project-wide suppression

Add the diagnostic ID to `<NoWarn>` in your project file:

```xml
<PropertyGroup>
  <NoWarn>$(NoWarn);MCPEXP001</NoWarn>
</PropertyGroup>
```

### Per-call suppression

Use `#pragma warning disable` around specific call sites:

```csharp
#pragma warning disable MCPEXP001
tool.Execution = new ToolExecution { ... };
#pragma warning restore MCPEXP001
```

For a full list of experimental diagnostic IDs and their descriptions, see the [list of diagnostics](list-of-diagnostics.md#experimental-apis).

## Serialization behavior

Experimental properties on protocol types are fully serialized and deserialized when using the SDK's built-in serialization via <xref:ModelContextProtocol.McpJsonUtilities.DefaultOptions>. This means experimental data is transmitted on the wire even if your application code doesn't directly interact with it, preserving protocol compatibility.

### Custom `JsonSerializerContext`

If you define your own `JsonSerializerContext` that includes MCP protocol types, experimental properties will not be included in your context's serialization contract. This is by design, as it protects your compiled code against binary breaking changes to experimental APIs.

To ensure consistent serialization behavior that always includes experimental properties, configure a `TypeInfoResolverChain` so the SDK's resolver handles MCP types:

```csharp
using ModelContextProtocol;

JsonSerializerOptions options = new()
{
    TypeInfoResolverChain =
    {
        McpJsonUtilities.DefaultOptions.TypeInfoResolver!,
        MyCustomContext.Default,
    }
};
```

By placing the SDK's resolver first, MCP types are serialized using the SDK's contract (which includes experimental properties), while your custom context handles your own types. This is recommended even if you aren't currently using experimental APIs, since it ensures your serialization configuration remains correct as new experimental properties are introduced or as you adopt experimental features in the future.

## See also

- [Versioning](versioning.md)
- [List of diagnostics](list-of-diagnostics.md#experimental-apis)
- [Tasks](concepts/tasks/tasks.md) (an experimental feature)
