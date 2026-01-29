using ModelContextProtocol.AspNetCore;

namespace Microsoft.AspNetCore.Builder;

/// <summary>
/// Extension methods for adding MCP middleware to an <see cref="IApplicationBuilder"/>.
/// </summary>
public static class McpApplicationBuilderExtensions
{
    /// <summary>
    /// Adds DNS rebinding protection middleware for MCP servers running on localhost.
    /// </summary>
    /// <param name="app">The <see cref="IApplicationBuilder"/>.</param>
    /// <returns>The <see cref="IApplicationBuilder"/> for chaining.</returns>
    /// <remarks>
    /// <para>
    /// This method provides protection against DNS rebinding attacks by validating that both
    /// Host and Origin headers (when present) resolve to localhost addresses.
    /// </para>
    /// <para>
    /// DNS rebinding attacks can allow malicious websites to bypass browser same-origin policy and make requests
    /// to localhost services. This protection is recommended for any MCP server that binds to localhost.
    /// </para>
    /// <para>
    /// For more information, see the <see href="https://github.com/modelcontextprotocol/typescript-sdk/security/advisories/GHSA-w48q-cv73-mx4w">MCP SDK security advisory</see>.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// var builder = WebApplication.CreateBuilder(args);
    /// builder.Services.AddMcpServer().WithHttpTransport();
    /// 
    /// var app = builder.Build();
    /// app.UseMcpDnsRebindingProtection(); // Add before MapMcp()
    /// app.MapMcp();
    /// app.Run();
    /// </code>
    /// </example>
    public static IApplicationBuilder UseMcpDnsRebindingProtection(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.UseMiddleware<DnsRebindingProtectionMiddleware>();

        return app;
    }
}
