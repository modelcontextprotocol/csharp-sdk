---
title: Experimental APIs
author: MackinnonBuck
description: Working with experimental APIs in the MCP C# SDK
uid: experimental
---

The Model Context Protocol C# SDK uses the [`[Experimental]`](https://learn.microsoft.com/dotnet/api/system.diagnostics.codeanalysis.experimentalattribute) attribute to mark APIs that are still in development and may change without notice. For more details on the SDK's versioning policy around experimental APIs, see the [Versioning](versioning.md) documentation.

## Suppressing experimental diagnostics

When you use an experimental API, the compiler produces a diagnostic (for example, `MCPEXP001`) to ensure you're aware the API might change. If you want to use the API, suppress the diagnostic in one of these ways:

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
#pragma warning disable MCPEXP002 // RunSessionHandler is experimental and may change.
options.RunSessionHandler = static (_, _, _) => Task.CompletedTask;
#pragma warning restore MCPEXP002
```

For a full list of experimental diagnostic IDs and their descriptions, see the [list of diagnostics](list-of-diagnostics.md#experimental-apis).

## Serialization behavior

Experimental properties on protocol types are fully serialized and deserialized when using the SDK's built-in serialization via <xref:ModelContextProtocol.McpJsonUtilities.DefaultOptions>. This means experimental data is transmitted on the wire even if your application code doesn't directly interact with it, preserving protocol compatibility.

The behavior of experimental properties differs depending on whether you use [reflection-based or source-generated](https://learn.microsoft.com/dotnet/standard/serialization/system-text-json/source-generation) serialization:

- **Reflection-based serialization** (the default when no `JsonSerializerContext` is used): Experimental properties are included. No special configuration is needed.
- **Source-generated serialization** (using a custom `JsonSerializerContext`): Experimental properties are **not** included in your context's serialization contract. This is by design, as it protects your compiled code against binary breaking changes to experimental APIs.

This means that switching between reflection-based and source-generated serialization can silently change which properties are serialized. To avoid this, source-generation users should configure a `TypeInfoResolverChain` as described below.

### Custom `JsonSerializerContext`

If you define your own `JsonSerializerContext` that includes MCP protocol types, configure a `TypeInfoResolverChain` so the SDK's resolver handles MCP types:

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

### Customizing nested content contracts

If you intentionally override the SDK's contracts for nested `Icon` or `Annotations` values, place your custom resolver before the SDK's resolver. Unlike the SDK-first configuration above, this selects your contracts, so you are responsible for preserving any experimental properties you need.

When combining these customizations with an application-defined source-generated context, use `JsonSourceGenerationMode.Metadata` for every context that supplies an enclosing type, such as `CallToolResult`, not just the context that supplies `Icon` or `Annotations`:

```csharp
using ModelContextProtocol.Protocol;
using System.Text.Json.Serialization;

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(CallToolResult))]
[JsonSerializable(typeof(Icon))]
[JsonSerializable(typeof(Annotations))]
internal partial class CustomContentContext : JsonSerializerContext;
```

Apply your contract modifiers through the resolver used by the `JsonSerializerOptions` passed to both serialization and deserialization. The SDK's content converter uses caller-supplied icon and annotation contracts first and supplies SDK metadata for missing nested types.

The default source-generation mode can generate optimized serialization handlers for enclosing types. These handlers can pass their context's own options to the content converter, bypassing the caller's modified nested contracts. This can cause serialization to emit the original property names while deserialization expects the customized names, losing annotation values or rejecting icons with missing required properties. Changing only the nested context to metadata mode does not prevent the enclosing handler from doing this.

Metadata-mode source generation avoids this mismatch and remains compatible with Native AOT and reflection-disabled applications. This requirement applies to intentional nested-contract customizations with caller-owned contexts; ordinary use of the SDK's serializer presets does not require a custom context. Keep the specification-defined property names when exchanging MCP protocol messages.

## See also

- [Versioning](versioning.md)
- [List of diagnostics](list-of-diagnostics.md#experimental-apis)
