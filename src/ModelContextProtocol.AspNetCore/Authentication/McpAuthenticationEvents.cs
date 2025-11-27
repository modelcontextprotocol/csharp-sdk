namespace ModelContextProtocol.AspNetCore.Authentication;

/// <summary>
/// Represents the authentication events for Model Context Protocol.
/// </summary>
public class McpAuthenticationEvents
{
    /// <summary>
    /// Gets or sets the function that's invoked when resource metadata is requested.
    /// </summary>
    /// <remarks>
    /// This function is called when a resource metadata request is made to the protected resource metadata endpoint.
    /// The implementer should set the <see cref="ResourceMetadataRequestContext.ResourceMetadata"/> property
    /// to provide the appropriate metadata for the current request.
    /// </remarks>
    public Func<ResourceMetadataRequestContext, Task> OnResourceMetadataRequest { get; set; } = context => Task.CompletedTask;
}
