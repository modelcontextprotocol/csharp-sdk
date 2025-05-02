using Microsoft.AspNetCore.Http;

namespace ModelContextProtocol.AspNetCore.Auth;

/// <summary>
/// Handles the resource metadata endpoint requests in an AOT-compatible way.
/// </summary>
internal sealed class ResourceMetadataEndpointHandler
{
    private readonly ResourceMetadataService _resourceMetadataService;
    
    public ResourceMetadataEndpointHandler(ResourceMetadataService resourceMetadataService)
    {
        _resourceMetadataService = resourceMetadataService;
    }
    
    public Task HandleRequest(HttpContext httpContext)
    {
        var result = _resourceMetadataService.HandleResourceMetadataRequest(httpContext);
        return result.ExecuteAsync(httpContext);
    }
}