using System.Diagnostics.CodeAnalysis;
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
internal sealed class AuthorizationFilterSetup(IAuthorizationPolicyProvider? policyProvider = null) : IConfigureOptions<McpServerOptions>, IPostConfigureOptions<McpServerOptions>
{
    private static readonly string AuthorizationFilterInvokedKey = "ModelContextProtocol.AspNetCore.AuthorizationFilter.Invoked";

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

    public void PostConfigure(string? name, McpServerOptions options)
    {
        CheckListToolsFilter(options);
        CheckCallToolFilter(options);

        CheckListResourcesFilter(options);
        CheckListResourceTemplatesFilter(options);
        CheckReadResourceFilter(options);

        CheckListPromptsFilter(options);
        CheckGetPromptFilter(options);
    }

    private void ConfigureListToolsFilter(McpServerOptions options)
    {
        options.Filters.Request.ListToolsFilters.Add(next =>
        {
            var toolCollection = options.ToolCollection;
            return async (context, cancellationToken) =>
            {
                context.Items[AuthorizationFilterInvokedKey] = true;

                var result = await next(context, cancellationToken);
                await FilterAuthorizedItemsAsync(
                    result.Tools, tool => toolCollection is not null && toolCollection.TryGetPrimitive(tool.Name, out var serverTool) ? serverTool : null,
                    context.User, context.Services, context);
                return result;
            };
        });
    }

    private static void CheckListToolsFilter(McpServerOptions options)
    {
        options.Filters.Request.ListToolsFilters.Add(next =>
        {
            var toolCollection = options.ToolCollection;
            return async (context, cancellationToken) =>
            {
                var result = await next(context, cancellationToken);

                if (HasAuthorizationMetadata(result.Tools.Select(tool => toolCollection is not null && toolCollection.TryGetPrimitive(tool.Name, out var serverTool) ? serverTool : null))
                    && !context.Items.ContainsKey(AuthorizationFilterInvokedKey))
                {
                    throw new InvalidOperationException("Authorization filter was not invoked for tools/list operation, but authorization metadata was found on the tools. Ensure that AddAuthorizationFilters() is called on the IMcpServerBuilder to configure authorization filters.");
                }

                return result;
            };
        });
    }

    private void ConfigureCallToolFilter(McpServerOptions options)
    {
        options.Filters.Request.CallToolFilters.Add(next => async (context, cancellationToken) =>
        {
            var authResult = await GetAuthorizationResultAsync(context.User, context.MatchedPrimitive, context.Services, context);
            if (!authResult.Succeeded)
            {
                throw new McpProtocolException("Access forbidden: This tool requires authorization.", McpErrorCode.InvalidRequest);
            }

            context.Items[AuthorizationFilterInvokedKey] = true;

            return await next(context, cancellationToken);
        });
    }

    private static void CheckCallToolFilter(McpServerOptions options)
    {
        options.Filters.Request.CallToolFilters.Add(next => async (context, cancellationToken) =>
        {
            if (HasAuthorizationMetadata(context.MatchedPrimitive)
                && !context.Items.ContainsKey(AuthorizationFilterInvokedKey))
            {
                throw new InvalidOperationException("Authorization filter was not invoked for tools/call operation, but authorization metadata was found on the tool. Ensure that AddAuthorizationFilters() is called on the IMcpServerBuilder to configure authorization filters.");
            }

            return await next(context, cancellationToken);
        });
    }

    private void ConfigureListResourcesFilter(McpServerOptions options)
    {
        options.Filters.Request.ListResourcesFilters.Add(next =>
        {
            var resourceCollection = options.ResourceCollection;
            return async (context, cancellationToken) =>
            {
                context.Items[AuthorizationFilterInvokedKey] = true;

                var result = await next(context, cancellationToken);
                await FilterAuthorizedItemsAsync(
                    result.Resources, resource => resourceCollection is not null && resourceCollection.TryGetPrimitive(resource.Uri, out var serverResource) ? serverResource : null,
                    context.User, context.Services, context);
                return result;
            };
        });
    }

    private static void CheckListResourcesFilter(McpServerOptions options)
    {
        options.Filters.Request.ListResourcesFilters.Add(next =>
        {
            var resourceCollection = options.ResourceCollection;
            return async (context, cancellationToken) =>
            {
                var result = await next(context, cancellationToken);

                if (HasAuthorizationMetadata(result.Resources.Select(resource => resourceCollection is not null && resourceCollection.TryGetPrimitive(resource.Uri, out var serverResource) ? serverResource : null))
                    && !context.Items.ContainsKey(AuthorizationFilterInvokedKey))
                {
                    throw new InvalidOperationException("Authorization filter was not invoked for resources/list operation, but authorization metadata was found on the resources. Ensure that AddAuthorizationFilters() is called on the IMcpServerBuilder to configure authorization filters.");
                }

                return result;
            };
        });
    }

    private void ConfigureListResourceTemplatesFilter(McpServerOptions options)
    {
        options.Filters.Request.ListResourceTemplatesFilters.Add(next =>
        {
            var resourceCollection = options.ResourceCollection;
            return async (context, cancellationToken) =>
            {
                context.Items[AuthorizationFilterInvokedKey] = true;

                var result = await next(context, cancellationToken);
                await FilterAuthorizedItemsAsync(
                    result.ResourceTemplates, resourceTemplate => resourceCollection is not null && resourceCollection.TryGetPrimitive(resourceTemplate.UriTemplate, out var serverResource) ? serverResource : null,
                    context.User, context.Services, context);
                return result;
            };
        });
    }

    private static void CheckListResourceTemplatesFilter(McpServerOptions options)
    {
        options.Filters.Request.ListResourceTemplatesFilters.Add(next =>
        {
            var resourceCollection = options.ResourceCollection;
            return async (context, cancellationToken) =>
            {
                var result = await next(context, cancellationToken);

                if (HasAuthorizationMetadata(result.ResourceTemplates.Select(resourceTemplate => resourceCollection is not null && resourceCollection.TryGetPrimitive(resourceTemplate.UriTemplate, out var serverResource) ? serverResource : null))
                    && !context.Items.ContainsKey(AuthorizationFilterInvokedKey))
                {
                    throw new InvalidOperationException("Authorization filter was not invoked for resources/templates/list operation, but authorization metadata was found on the resource templates. Ensure that AddAuthorizationFilters() is called on the IMcpServerBuilder to configure authorization filters.");
                }

                return result;
            };
        });
    }

    private void ConfigureReadResourceFilter(McpServerOptions options)
    {
        options.Filters.Request.ReadResourceFilters.Add(next => async (context, cancellationToken) =>
        {
            context.Items[AuthorizationFilterInvokedKey] = true;

            var authResult = await GetAuthorizationResultAsync(context.User, context.MatchedPrimitive, context.Services, context);
            if (!authResult.Succeeded)
            {
                throw new McpProtocolException("Access forbidden: This resource requires authorization.", McpErrorCode.InvalidRequest);
            }

            return await next(context, cancellationToken);
        });
    }

    private static void CheckReadResourceFilter(McpServerOptions options)
    {
        options.Filters.Request.ReadResourceFilters.Add(next => async (context, cancellationToken) =>
        {
            if (HasAuthorizationMetadata(context.MatchedPrimitive)
                && !context.Items.ContainsKey(AuthorizationFilterInvokedKey))
            {
                throw new InvalidOperationException("Authorization filter was not invoked for resources/read operation, but authorization metadata was found on the resource. Ensure that AddAuthorizationFilters() is called on the IMcpServerBuilder to configure authorization filters.");
            }

            return await next(context, cancellationToken);
        });
    }

    private void ConfigureListPromptsFilter(McpServerOptions options)
    {
        options.Filters.Request.ListPromptsFilters.Add(next =>
        {
            var promptCollection = options.PromptCollection;
            return async (context, cancellationToken) =>
            {
                context.Items[AuthorizationFilterInvokedKey] = true;

                var result = await next(context, cancellationToken);
                await FilterAuthorizedItemsAsync(
                    result.Prompts, prompt => promptCollection is not null && promptCollection.TryGetPrimitive(prompt.Name, out var serverPrompt) ? serverPrompt : null,
                    context.User, context.Services, context);
                return result;
            };
        });
    }

    private static void CheckListPromptsFilter(McpServerOptions options)
    {
        options.Filters.Request.ListPromptsFilters.Add(next =>
        {
            var promptCollection = options.PromptCollection;
            return async (context, cancellationToken) =>
            {
                var result = await next(context, cancellationToken);

                if (HasAuthorizationMetadata(result.Prompts.Select(prompt => promptCollection is not null && promptCollection.TryGetPrimitive(prompt.Name, out var serverPrompt) ? serverPrompt : null))
                    && !context.Items.ContainsKey(AuthorizationFilterInvokedKey))
                {
                    throw new InvalidOperationException("Authorization filter was not invoked for prompts/list operation, but authorization metadata was found on the prompts. Ensure that AddAuthorizationFilters() is called on the IMcpServerBuilder to configure authorization filters.");
                }

                return result;
            };
        });
    }

    private void ConfigureGetPromptFilter(McpServerOptions options)
    {
        options.Filters.Request.GetPromptFilters.Add(next => async (context, cancellationToken) =>
        {
            context.Items[AuthorizationFilterInvokedKey] = true;

            var authResult = await GetAuthorizationResultAsync(context.User, context.MatchedPrimitive, context.Services, context);
            if (!authResult.Succeeded)
            {
                throw new McpProtocolException("Access forbidden: This prompt requires authorization.", McpErrorCode.InvalidRequest);
            }

            return await next(context, cancellationToken);
        });
    }

    private static void CheckGetPromptFilter(McpServerOptions options)
    {
        options.Filters.Request.GetPromptFilters.Add(next => async (context, cancellationToken) =>
        {
            if (HasAuthorizationMetadata(context.MatchedPrimitive)
                && !context.Items.ContainsKey(AuthorizationFilterInvokedKey))
            {
                throw new InvalidOperationException("Authorization filter was not invoked for prompts/get operation, but authorization metadata was found on the prompt. Ensure that AddAuthorizationFilters() is called on the IMcpServerBuilder to configure authorization filters.");
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
        if (!HasAuthorizationMetadata(primitive))
        {
            return AuthorizationResult.Success();
        }

        if (policyProvider is null)
        {
            throw new InvalidOperationException($"You must call AddAuthorization() because an authorization related attribute was found on {primitive.Id}");
        }

        var policy = await CombineAsync(policyProvider, primitive.Metadata);
        if (policy is null)
        {
            return AuthorizationResult.Success();
        }

        if (requestServices is null)
        {
            // The IAuthorizationPolicyProvider service must be non-null to get to this line, so it's very unexpected for RequestContext.Services to not be set.
            throw new InvalidOperationException("RequestContext.Services is not set! The McpServer must be initialized with a non-null IServiceProvider.");
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

    private static bool HasAuthorizationMetadata([NotNullWhen(true)] IMcpServerPrimitive? primitive)
    {
        // If no primitive was found for this request or there is IAllowAnonymous metadata anywhere on the class or method,
        // the request should go through as normal.
        if (primitive is null || primitive.Metadata.Any(static m => m is IAllowAnonymous))
        {
            return false;
        }

        return primitive.Metadata.Any(static m => m is IAuthorizeData or AuthorizationPolicy or IAuthorizationRequirementData);
    }

    private static bool HasAuthorizationMetadata(IEnumerable<IMcpServerPrimitive?> primitives)
        => primitives.Any(HasAuthorizationMetadata);
}