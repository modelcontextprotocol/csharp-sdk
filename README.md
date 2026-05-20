# MCP C# SDK

[![NuGet version](https://img.shields.io/nuget/v/ModelContextProtocol.svg)](https://www.nuget.org/packages/ModelContextProtocol)

The official C# SDK for the [Model Context Protocol](https://modelcontextprotocol.io/), enabling .NET applications, services, and libraries to implement and interact with MCP clients and servers. Please visit the [API documentation](https://csharp.sdk.modelcontextprotocol.io/api/ModelContextProtocol.html) for more details on available functionality.

## Packages

This SDK consists of three main packages:

- **[ModelContextProtocol.Core](https://www.nuget.org/packages/ModelContextProtocol.Core)** [![NuGet version](https://img.shields.io/nuget/v/ModelContextProtocol.Core.svg)](https://www.nuget.org/packages/ModelContextProtocol.Core) - For projects that only need to use the client or low-level server APIs and want the minimum number of dependencies.

- **[ModelContextProtocol](https://www.nuget.org/packages/ModelContextProtocol)** [![NuGet version](https://img.shields.io/nuget/v/ModelContextProtocol.svg)](https://www.nuget.org/packages/ModelContextProtocol) - The main package with hosting and dependency injection extensions. References `ModelContextProtocol.Core`. This is the right fit for most projects that don't need HTTP server capabilities.

- **[ModelContextProtocol.AspNetCore](https://www.nuget.org/packages/ModelContextProtocol.AspNetCore)** [![NuGet version](https://img.shields.io/nuget/v/ModelContextProtocol.AspNetCore.svg)](https://www.nuget.org/packages/ModelContextProtocol.AspNetCore) - The library for HTTP-based MCP servers. References `ModelContextProtocol`.

## Getting Started

To get started, see the [Getting Started](https://csharp.sdk.modelcontextprotocol.io/concepts/getting-started.html) guide in the conceptual documentation for installation instructions, package-selection guidance, and complete examples for both clients and servers.

You can also browse the [samples](samples) directory and the [API documentation](https://csharp.sdk.modelcontextprotocol.io/api/ModelContextProtocol.html) for more details on available functionality.

## About MCP

The Model Context Protocol (MCP) is an open protocol that standardizes how applications provide context to Large Language Models (LLMs). It enables secure integration between LLMs and various data sources and tools.

For more information about MCP:

- [Official MCP Documentation](https://modelcontextprotocol.io/)
- [MCP C# SDK Documentation](https://csharp.sdk.modelcontextprotocol.io/)
- [Protocol Specification](https://modelcontextprotocol.io/specification/)
- [GitHub Organization](https://github.com/modelcontextprotocol)

## Enterprise Auth / Enterprise Managed Authorization

The SDK provides Cross-Application Access support for the [Identity Assertion Authorization Grant flow](https://github.com/modelcontextprotocol/ext-auth/blob/main/specification/draft/enterprise-managed-authorization.mdx),
enabling enterprise SSO scenarios where users authenticate once via their enterprise Identity Provider and
access MCP servers without per-server authorization prompts.

The flow consists of two token operations:
1. **RFC 8693 Token Exchange** at the IdP: ID Token → JWT Authorization Grant (JAG)
2. **RFC 7523 JWT Bearer Grant** at the MCP Server: JAG → Access Token

### Using CrossApplicationAccessProvider

```csharp
using ModelContextProtocol.Authentication;

var provider = new CrossApplicationAccessProvider(new CrossApplicationAccessProviderOptions
{
    ClientId = "mcp-client-id",
    IdpTokenEndpoint = "https://company.okta.com/oauth2/token",
    IdpClientId = "idp-client-id",
    IdTokenCallback = async (context, ct) =>
    {
        // Return the OIDC ID token from your SSO session
        return myIdToken;
    }
});

var tokens = await provider.GetAccessTokenAsync(
    resourceUrl: new Uri("https://mcp-server.example.com"),
    authorizationServerUrl: new Uri("https://auth.mcp-server.example.com"));
```

## License

This project is licensed under the [Apache License 2.0](LICENSE).
