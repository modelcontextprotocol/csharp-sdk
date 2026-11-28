using Microsoft.Extensions.Options;

namespace ModelContextProtocol.AspNetCore.Authentication;

internal sealed class McpOAuthProxyOptionsValidator : IValidateOptions<McpOAuthProxyOptions>
{
    private static readonly HashSet<string> ReservedAuthorizationParameters = new(StringComparer.Ordinal)
    {
        "client_id", "redirect_uri", "response_type", "code_challenge", "code_challenge_method",
        "scope", "state", "resource"
    };

    private static readonly HashSet<string> ReservedTokenParameters = new(StringComparer.Ordinal)
    {
        "client_id", "client_secret", "redirect_uri", "grant_type", "code", "code_verifier",
        "refresh_token", "resource", "scope"
    };

    public ValidateOptionsResult Validate(string? name, McpOAuthProxyOptions options)
    {
        List<string> failures = [];

        ValidateSecureUri(options.Issuer, nameof(options.Issuer), failures);
        if (options.Issuer is not null && (!string.IsNullOrEmpty(options.Issuer.Query) || !string.IsNullOrEmpty(options.Issuer.Fragment)))
        {
            failures.Add($"{nameof(options.Issuer)} must not contain a query or fragment.");
        }

        ValidateSecureUri(options.UpstreamRedirectUri, nameof(options.UpstreamRedirectUri), failures);

        var hasExplicitAuthorizationEndpoint = options.UpstreamAuthorizationEndpoint is not null;
        var hasExplicitTokenEndpoint = options.UpstreamTokenEndpoint is not null;
        if (hasExplicitAuthorizationEndpoint != hasExplicitTokenEndpoint)
        {
            failures.Add($"{nameof(options.UpstreamAuthorizationEndpoint)} and {nameof(options.UpstreamTokenEndpoint)} must be configured together.");
        }
        else if (hasExplicitAuthorizationEndpoint)
        {
            ValidateSecureUri(options.UpstreamAuthorizationEndpoint, nameof(options.UpstreamAuthorizationEndpoint), failures);
            ValidateSecureUri(options.UpstreamTokenEndpoint, nameof(options.UpstreamTokenEndpoint), failures);
        }
        else
        {
            ValidateSecureUri(options.UpstreamIssuer, nameof(options.UpstreamIssuer), failures);
        }

        if (string.IsNullOrWhiteSpace(options.UpstreamClientId))
        {
            failures.Add($"{nameof(options.UpstreamClientId)} is required.");
        }

        if (options.UpstreamClientAuthenticationMethod is not McpOAuthProxyClientAuthenticationMethod.None &&
            string.IsNullOrEmpty(options.UpstreamClientSecret))
        {
            failures.Add($"{nameof(options.UpstreamClientSecret)} is required for the selected upstream client authentication method.");
        }

        if (options.TokenFactory is null)
        {
            failures.Add($"{nameof(options.TokenFactory)} is required.");
        }

        if (options.ClientRegistrationValidator is null)
        {
            failures.Add($"{nameof(options.ClientRegistrationValidator)} is required.");
        }

        if (options.AuthorizationValidator is null)
        {
            failures.Add($"{nameof(options.AuthorizationValidator)} is required.");
        }

        ValidatePositiveLifetime(options.MaximumAccessTokenLifetime, nameof(options.MaximumAccessTokenLifetime), failures);
        ValidatePositiveLifetime(options.AuthorizationTransactionLifetime, nameof(options.AuthorizationTransactionLifetime), failures);
        ValidatePositiveLifetime(options.AuthorizationCodeLifetime, nameof(options.AuthorizationCodeLifetime), failures);
        ValidatePositiveLifetime(options.ClientRegistrationLifetime, nameof(options.ClientRegistrationLifetime), failures);
        ValidatePositiveLifetime(options.RefreshTokenLifetime, nameof(options.RefreshTokenLifetime), failures);

        foreach (var scope in options.AllowedScopes)
        {
            if (string.IsNullOrWhiteSpace(scope) || scope.Any(char.IsWhiteSpace))
            {
                failures.Add($"{nameof(options.AllowedScopes)} contains an invalid scope value.");
                break;
            }
        }

        foreach (var resource in options.AllowedResources)
        {
            if (!Uri.TryCreate(resource, UriKind.Absolute, out var resourceUri) ||
                !McpOAuthProxyUtilities.IsSecureEndpoint(resourceUri) ||
                !string.IsNullOrEmpty(resourceUri.Fragment))
            {
                failures.Add($"{nameof(options.AllowedResources)} contains an invalid resource URI.");
                break;
            }
        }

        if (options.AdditionalAuthorizationParameters.Keys.Any(ReservedAuthorizationParameters.Contains))
        {
            failures.Add($"{nameof(options.AdditionalAuthorizationParameters)} cannot override standard OAuth parameters.");
        }

        if (options.AdditionalTokenParameters.Keys.Any(ReservedTokenParameters.Contains))
        {
            failures.Add($"{nameof(options.AdditionalTokenParameters)} cannot override standard OAuth parameters.");
        }

        if (options.AdditionalAuthorizationParameters.Keys.Any(string.IsNullOrWhiteSpace) ||
            options.AdditionalTokenParameters.Keys.Any(string.IsNullOrWhiteSpace))
        {
            failures.Add("Additional OAuth parameter names cannot be empty.");
        }

        if (options.JwksUri is not null)
        {
            ValidateSecureUri(options.JwksUri, nameof(options.JwksUri), failures);
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    private static void ValidateSecureUri(Uri? uri, string name, List<string> failures)
    {
        if (uri is null)
        {
            failures.Add($"{name} is required.");
        }
        else if (!McpOAuthProxyUtilities.IsSecureEndpoint(uri))
        {
            failures.Add($"{name} must be an absolute HTTPS URI, or an HTTP loopback URI for development.");
        }
    }

    private static void ValidatePositiveLifetime(TimeSpan lifetime, string name, List<string> failures)
    {
        if (lifetime <= TimeSpan.Zero)
        {
            failures.Add($"{name} must be positive.");
        }
    }
}
