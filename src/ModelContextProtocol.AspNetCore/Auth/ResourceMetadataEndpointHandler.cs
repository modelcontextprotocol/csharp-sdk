using Microsoft.AspNetCore.Http;
using ModelContextProtocol.AspNetCore.Auth;

namespace Microsoft.AspNetCore.Builder;

public static partial class McpEndpointRouteBuilderExtensions
{
    // This class handles the resource metadata endpoint in an AOT-compatible way
    private sealed class ResourceMetadataEndpointHandler
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
}
