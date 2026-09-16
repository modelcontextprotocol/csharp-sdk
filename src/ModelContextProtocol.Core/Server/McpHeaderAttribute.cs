using ModelContextProtocol.Client;

namespace ModelContextProtocol.Server;

/// <summary>
/// Indicates that a tool parameter should be mirrored as an HTTP header in client requests.
/// </summary>
/// <remarks>
/// <para>
/// When applied to a parameter, the SDK will include an <c>x-mcp-header</c> extension property
/// in the parameter's JSON schema. Clients will then mirror this parameter's value into an
/// HTTP header named <c>Mcp-Param-{Name}</c>.
/// </para>
/// <para>
/// Only parameters with primitive types (integer, string, boolean) may use this attribute.
/// The header name must match HTTP field-name token syntax (tchar per RFC 9110 Section 5.6.2)
/// and must be case-insensitively unique within the tool's input schema.
/// </para>
/// <para>
/// This enables network infrastructure such as load balancers, proxies, and gateways to make
/// routing decisions based on tool parameter values without parsing the JSON-RPC request body.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// [McpServerTool]
/// public static string ExecuteSql(
///     [McpHeader("Region")] string region,
///     string query)
/// {
///     // The client will add header: Mcp-Param-Region: {region value}
/// }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property)]
public sealed class McpHeaderAttribute : Attribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="McpHeaderAttribute"/> class.
    /// </summary>
    /// <param name="name">
    /// The name portion of the header. The full header name will be <c>Mcp-Param-{name}</c>.
    /// Must match HTTP field-name token syntax (tchar per RFC 9110 Section 5.6.2).
    /// </param>
    /// <exception cref="ArgumentException">
    /// The name is null, empty, or contains invalid characters.
    /// </exception>
    public McpHeaderAttribute(string name)
    {
        Throw.IfNullOrWhiteSpace(name);
        ValidateHeaderName(name);
        Name = name;
    }

    /// <summary>
    /// Gets the name portion of the header.
    /// </summary>
    /// <remarks>
    /// The full header name sent by clients will be <c>Mcp-Param-{Name}</c>.
    /// </remarks>
    public string Name { get; }

    /// <summary>
    /// Validates that a header name contains only valid HTTP token characters (tchar) per RFC 9110 Section 5.6.2.
    /// </summary>
    /// <param name="name">The header name to validate.</param>
    /// <exception cref="ArgumentException">The name contains invalid characters.</exception>
    internal static void ValidateHeaderName(string name)
    {
        int idx = McpHeaderExtractor.FindFirstNonTchar(name);
        if (idx >= 0)
        {
            char c = name[idx];
            throw new ArgumentException(
                $"Header name contains invalid character '{c}' (0x{(int)c:X2}). " +
                "Only HTTP token characters (tchar per RFC 9110 Section 5.6.2) are allowed.",
                nameof(name));
        }
    }
}
