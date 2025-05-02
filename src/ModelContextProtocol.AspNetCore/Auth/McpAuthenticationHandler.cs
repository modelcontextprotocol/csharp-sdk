using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.Encodings.Web;

namespace ModelContextProtocol.AspNetCore.Auth;

/// <summary>
/// Authentication handler for MCP protocol that adds resource metadata to challenge responses.
/// </summary>
public class McpAuthenticationHandler : AuthenticationHandler<McpAuthenticationOptions>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="McpAuthenticationHandler"/> class.
    /// </summary>
    public McpAuthenticationHandler(
        IOptionsMonitor<McpAuthenticationOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    /// <inheritdoc />
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        // This handler doesn't perform authentication - it only adds resource metadata to challenges
        // The actual authentication will be handled by the bearer token handler or other authentication handlers
        return Task.FromResult(AuthenticateResult.NoResult());
    }

    /// <inheritdoc />
    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        if (Options.ResourceMetadataUri != null)
        {
            Response.Headers.WWWAuthenticate = $"Bearer resource_metadata=\"{Options.ResourceMetadataUri}\"";
        }
        
        return Task.CompletedTask;
    }
}

/// <summary>
/// Options for the MCP authentication handler.
/// </summary>
public class McpAuthenticationOptions : AuthenticationSchemeOptions
{
    /// <summary>
    /// The URI to the resource metadata document.
    /// </summary>
    public Uri? ResourceMetadataUri { get; set; }
}