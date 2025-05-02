using Microsoft.AspNetCore.Http;
using ModelContextProtocol.Auth.Types;
using ModelContextProtocol.Utils.Json;

namespace ModelContextProtocol.AspNetCore.Auth;

/// <summary>
/// Service for managing MCP resource metadata.
/// </summary>
public class ResourceMetadataService
{
    private readonly ProtectedResourceMetadata _metadata = new();

    /// <summary>
    /// Configures the resource metadata.
    /// </summary>
    /// <param name="configure">Configuration action.</param>
    public void ConfigureMetadata(Action<ProtectedResourceMetadata> configure)
    {
        configure(_metadata);
    }

    /// <summary>
    /// Gets the resource metadata.
    /// </summary>
    /// <returns>The resource metadata.</returns>
    public ProtectedResourceMetadata GetMetadata()
    {
        return _metadata;
    }
    
    /// <summary>
    /// Handles the resource metadata request.
    /// </summary>
    /// <param name="context">The HTTP context.</param>
    /// <returns>An IResult containing the resource metadata.</returns>
    public IResult HandleResourceMetadataRequest(HttpContext context)
    {
        var metadata = GetMetadata();
        
        // Set default resource if not set
        if (metadata.Resource == null)
        {
            var request = context.Request;
            var hostString = request.Host.Value;
            var scheme = request.Scheme;
            metadata.Resource = new Uri($"{scheme}://{hostString}");
        }
        
        return Results.Json(metadata, McpJsonUtilities.DefaultOptions.GetTypeInfo(typeof(ProtectedResourceMetadata)));
    }
}