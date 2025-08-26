using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace ModelContextProtocol.AspNetCore;

/// <summary>
/// Evaluates authorization policies from endpoint metadata.
/// </summary>
internal sealed class AuthorizationFilterSetup(IAuthorizationPolicyProvider? policyProvider = null) : IConfigureOptions<McpServerOptions>
{
    public void Configure(McpServerOptions options)
    {
        ConfigureListToolsFilter(options);
        ConfigureCallToolFilter(options);

        ConfigureListResourcesFilter(options);
        ConfigureListResourceTemplatesFilter(options);
        ConfigureReadResourceFilter(options);

        ConfigureListPromptsFilter(options);
        ConfigureGetPromptFilter(options);
    }

    private void ConfigureListToolsFilter(McpServerOptions options)
    {
        options.Filters.ListToolsFilters.Add(next => async (context, cancellationToken) =>
        {
            var result = await next(context, cancellationToken);
            await FilterAuthorizedItemsAsync(
                result.Tools, static tool => tool.McpServerTool,
                context.User, context.Services, context);
            return result;
        });
    }

    private void ConfigureCallToolFilter(McpServerOptions options)
    {
        options.Filters.CallToolFilters.Add(next => async (context, cancellationToken) =>
        {
            var authResult = await GetAuthorizationResultAsync(context.User, context.MatchedPrimitive, context.Services, context);
            if (!authResult.Succeeded)
            {
                return new CallToolResult
                {
                    Content = [new TextContentBlock { Text = "Access forbidden: This tool requires authorization." }],
                    IsError = true
                };
            }

            return await next(context, cancellationToken);
        });
    }

    private void ConfigureListResourcesFilter(McpServerOptions options)
    {
        options.Filters.ListResourcesFilters.Add(next => async (context, cancellationToken) =>
        {
            var result = await next(context, cancellationToken);
            await FilterAuthorizedItemsAsync(
                result.Resources, static resource => resource.McpServerResource,
                context.User, context.Services, context);
            return result;
        });
    }

    private void ConfigureListResourceTemplatesFilter(McpServerOptions options)
    {
        options.Filters.ListResourceTemplatesFilters.Add(next => async (context, cancellationToken) =>
        {
            var result = await next(context, cancellationToken);
            await FilterAuthorizedItemsAsync(
                result.ResourceTemplates, static resourceTemplate => resourceTemplate.McpServerResource,
                context.User, context.Services, context);
            return result;
        });
    }

    private void ConfigureReadResourceFilter(McpServerOptions options)
    {
        options.Filters.ReadResourceFilters.Add(next => async (context, cancellationToken) =>
        {
            var authResult = await GetAuthorizationResultAsync(context.User, context.MatchedPrimitive, context.Services, context);
            if (!authResult.Succeeded)
            {
                throw new McpException("Access forbidden: This resource requires authorization.", McpErrorCode.InvalidRequest);
            }

            return await next(context, cancellationToken);
        });
    }

    private void ConfigureListPromptsFilter(McpServerOptions options)
    {
        options.Filters.ListPromptsFilters.Add(next => async (context, cancellationToken) =>
        {
            var result = await next(context, cancellationToken);
            await FilterAuthorizedItemsAsync(
                result.Prompts, static prompt => prompt.McpServerPrompt,
                context.User, context.Services, context);
            return result;
        });
    }

    private void ConfigureGetPromptFilter(McpServerOptions options)
    {
        options.Filters.GetPromptFilters.Add(next => async (context, cancellationToken) =>
        {
            var authResult = await GetAuthorizationResultAsync(context.User, context.MatchedPrimitive, context.Services, context);
            if (!authResult.Succeeded)
            {
                throw new McpException("Access forbidden: This prompt requires authorization.", McpErrorCode.InvalidRequest);
            }

            return await next(context, cancellationToken);
        });
    }

    /// <summary>
    /// Filters a collection of items based on authorization policies in their metadata.
    /// For list operations where we need to filter results by authorization.
    /// </summary>
    private async ValueTask FilterAuthorizedItemsAsync<T>(IList<T> items, Func<T, IMcpServerPrimitive?> primitiveSelector,
        ClaimsPrincipal? user, IServiceProvider? requestServices, object context)
    {
        for (int i = items.Count - 1; i >= 0; i--)
        {
            var authorizationResult = await GetAuthorizationResultAsync(
                user, primitiveSelector(items[i]), requestServices, context);

            if (!authorizationResult.Succeeded)
            {
                items.RemoveAt(i);
            }
        }
    }

    private async ValueTask<AuthorizationResult> GetAuthorizationResultAsync(
        ClaimsPrincipal? user, IMcpServerPrimitive? primitive, IServiceProvider? requestServices, object context)
    {
        // If no primitive was found for this request or there is IAllowAnonymous metadata anywhere on the class or method,
        // the request should go through as normal.
        if (primitive is null || primitive.Metadata.Any(static m => m is IAllowAnonymous))
        {
            return AuthorizationResult.Success();
        }

        // There are no [Authorize] style attributes applied to the method or containing class. Any fallback policies
        // have already been enforced at the HTTP request level by the ASP.NET Core authorization middleware.
        if (!primitive.Metadata.Any(static m => m is IAuthorizeData or AuthorizationPolicy or IAuthorizationRequirementData))
        {
            return AuthorizationResult.Success();
        }

        if (policyProvider is null)
        {
            throw new InvalidOperationException($"You must call AddAuthorization() because an authorization related attribute was found on {primitive.Id}");
        }

        // TODO: Cache policy lookup. We would probably use a singleton (not-static) ConditionalWeakTable<IMcpServerPrimitive, AuthorizationPolicy?>.
        var policy = await CombineAsync(policyProvider, primitive.Metadata);
        if (policy is null)
        {
            return AuthorizationResult.Success();
        }

        if (requestServices is null)
        {
            // The IAuthorizationPolicyProvider service must be non-null to get to this line, so it's very unexpected for RequestContext.Services to not be set.
            throw new InvalidOperationException("RequestContext.Services is not set! The IMcpServer must be initialized with a non-null IServiceProvider.");
        }

        // ASP.NET Core's AuthorizationMiddleware resolves the IAuthorizationService from scoped request services, so we do the same.
        var authService = requestServices.GetRequiredService<IAuthorizationService>();
        return await authService.AuthorizeAsync(user ?? new ClaimsPrincipal(new ClaimsIdentity()), context, policy);
    }

    /// <summary>
    /// Combines authorization policies and requirements from endpoint metadata without considering <see cref="IAllowAnonymous"/>.
    /// </summary>
    /// <param name="policyProvider">The authorization policy provider.</param>
    /// <param name="endpointMetadata">The endpoint metadata collection.</param>
    /// <returns>The combined authorization policy, or null if no authorization is required.</returns>
    private static async ValueTask<AuthorizationPolicy?> CombineAsync(IAuthorizationPolicyProvider policyProvider, IReadOnlyList<object> endpointMetadata)
    {
        // https://github.com/dotnet/aspnetcore/issues/63365 tracks adding this as public API to AuthorizationPolicy itself.
        // Copied from https://github.com/dotnet/aspnetcore/blob/9f2977bf9cfb539820983bda3bedf81c8cda9f20/src/Security/Authorization/Policy/src/AuthorizationMiddleware.cs#L116-L138
        var authorizeData = endpointMetadata.OfType<IAuthorizeData>();
        var policies = endpointMetadata.OfType<AuthorizationPolicy>();

        var policy = await AuthorizationPolicy.CombineAsync(policyProvider, authorizeData, policies);

        AuthorizationPolicyBuilder? reqPolicyBuilder = null;

        foreach (var m in endpointMetadata)
        {
            if (m is not IAuthorizationRequirementData requirementData)
            {
                continue;
            }

            reqPolicyBuilder ??= new AuthorizationPolicyBuilder();
            foreach (var requirement in requirementData.GetRequirements())
            {
                reqPolicyBuilder.AddRequirements(requirement);
            }
        }

        if (reqPolicyBuilder is null)
        {
            return policy;
        }

        // Combine policy with requirements or just use requirements if no policy
        return (policy is null)
            ? reqPolicyBuilder.Build()
            : AuthorizationPolicy.Combine(policy, reqPolicyBuilder.Build());
    }
}