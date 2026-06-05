# Cross-SDK Repositories

This reference lists all official MCP SDK repositories and the themes to search for when cross-referencing issues.

## MCP SDK Repositories

| SDK | Repository | Tier | Tier Tracking Issue |
|---|---|---|---|
| TypeScript | [modelcontextprotocol/typescript-sdk](https://github.com/modelcontextprotocol/typescript-sdk) | Tier 1 | [modelcontextprotocol#2271](https://github.com/modelcontextprotocol/modelcontextprotocol/issues/2271) |
| Python | [modelcontextprotocol/python-sdk](https://github.com/modelcontextprotocol/python-sdk) | Tier 1 | [modelcontextprotocol#2304](https://github.com/modelcontextprotocol/modelcontextprotocol/issues/2304) |
| Java | [modelcontextprotocol/java-sdk](https://github.com/modelcontextprotocol/java-sdk) | Tier 2 | [modelcontextprotocol#2301](https://github.com/modelcontextprotocol/modelcontextprotocol/issues/2301) |
| C# | [modelcontextprotocol/csharp-sdk](https://github.com/modelcontextprotocol/csharp-sdk) | Tier 1 | [modelcontextprotocol#2261](https://github.com/modelcontextprotocol/modelcontextprotocol/issues/2261) |
| Go | [modelcontextprotocol/go-sdk](https://github.com/modelcontextprotocol/go-sdk) | Tier 1 | [modelcontextprotocol#2279](https://github.com/modelcontextprotocol/modelcontextprotocol/issues/2279) |
| Kotlin | [modelcontextprotocol/kotlin-sdk](https://github.com/modelcontextprotocol/kotlin-sdk) | TBD | — |
| Swift | [modelcontextprotocol/swift-sdk](https://github.com/modelcontextprotocol/swift-sdk) | Tier 3 | [modelcontextprotocol#2309](https://github.com/modelcontextprotocol/modelcontextprotocol/issues/2309) |
| Rust | [modelcontextprotocol/rust-sdk](https://github.com/modelcontextprotocol/rust-sdk) | Tier 2 | [modelcontextprotocol#2346](https://github.com/modelcontextprotocol/modelcontextprotocol/issues/2346) |
| Ruby | [modelcontextprotocol/ruby-sdk](https://github.com/modelcontextprotocol/ruby-sdk) | Tier 3 | [modelcontextprotocol#2340](https://github.com/modelcontextprotocol/modelcontextprotocol/issues/2340) |
| PHP | [modelcontextprotocol/php-sdk](https://github.com/modelcontextprotocol/php-sdk) | Tier 3 | [modelcontextprotocol#2305](https://github.com/modelcontextprotocol/modelcontextprotocol/issues/2305) |

**Live SDK list URL:** `https://raw.githubusercontent.com/modelcontextprotocol/modelcontextprotocol/refs/heads/main/docs/docs/sdk.mdx`

## Cross-Reference Themes

When cross-referencing issues across SDK repos, search open issues for these themes. Use the keyword patterns to match against issue titles and the first 500 characters of issue bodies.

| Theme | Search Keywords |
|---|---|
| **OAuth / Authorization** | `oauth`, `authorization`, `auth`, `JWT`, `token`, `Entra`, `OIDC`, `PKCE`, `code_challenge`, `scope`, `WWW-Authenticate`, `bearer`, `client credentials` |
| **SSE / Keep-Alive** | `SSE`, `server-sent`, `keep-alive`, `keepalive`, `heartbeat`, `event-stream` |
| **Streamable HTTP** | `streamable http`, `HTTP stream`, `stateless`, `stateful`, `session ID` |
| **Dynamic Tools** | `dynamic tool`, `tool filter`, `tool registration`, `runtime tool`, `list_changed` |
| **JSON Serialization** | `JSON serial`, `serializ`, `deserializ`, `JsonSerializer`, `schema` |
| **Code Signing** | `code sign`, `sign binaries`, `DLL sign`, `strong name` |
| **Resource Disposal** | `resource dispos`, `dispos resource`, `resource leak`, `memory leak` |
| **Multiple Endpoints** | `multiple endpoint`, `multiple server`, `multi-server`, `keyed service` |
| **Structured Content / Output** | `structured content`, `output schema`, `structuredContent` |
| **Reconnection / Resumption** | `reconnect`, `resume`, `resumption`, `session recovery` |
| **MCP Apps / Tasks** | `MCP App`, `task`, `elicitation`, `sampling` |
| **SEP Implementations** | `SEP-990`, `SEP-1046`, `SEP-985`, `SEP-991`, `SEP-835`, `SEP-1686`, `SEP-1699` |

## Cross-Reference Usage

For each theme:
1. Search open issues in each non-C# SDK repo using the keyword patterns
2. Match C# SDK issues to related issues in other repos
3. Present as themed tables in the report

When a C# SDK issue has a clear counterpart (same SEP number, same feature request, same bug pattern), link them. Don't force connections where the relationship is tenuous.
