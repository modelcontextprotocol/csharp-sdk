using Microsoft.Extensions.Options;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace ModelContextProtocol.AspNetCore;

internal sealed class AuthorizationFiltersMarker;

internal sealed class AuthorizationCallToolFilterGuardSetup(AuthorizationFiltersMarker? marker = null) : IPostConfigureOptions<McpServerOptions>
{
    public void PostConfigure(string? name, McpServerOptions options)
    {
        if (marker is not null)
        {
            return;
        }

#pragma warning disable MCPEXP002 // The guard must run before Tasks can dispatch the request.
        options.Filters.Request.CallToolWithAlternateFilters.Insert(0, static async (context, next, cancellationToken) =>
        {
            if (AuthorizationFilterSetup.HasAuthorizationMetadata(context.MatchedPrimitive))
            {
                throw new InvalidOperationException(
                    "Authorization filter was not invoked for tools/call operation, but authorization metadata was found on the tool. " +
                    "Ensure that AddAuthorizationFilters() is called on the IMcpServerBuilder to configure authorization filters.");
            }

            return await next(context, cancellationToken);
        });
#pragma warning restore MCPEXP002
    }
}
