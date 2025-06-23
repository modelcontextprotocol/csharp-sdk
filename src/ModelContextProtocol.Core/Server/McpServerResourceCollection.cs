namespace ModelContextProtocol.Server;

/// <summary>Provides a thread-safe collection of <see cref="McpServerResource"/> instances, indexed by their URI templates.</summary>
public sealed class McpServerResourceCollection()
    : McpServerPrimitiveCollection<McpServerResource>(UriTemplate.UriTemplateComparer.Instance);