using ModelContextProtocol.Protocol.Types;
using ModelContextProtocol.Utils;

namespace ModelContextProtocol.Server;

/// <summary>Provides an <see cref="McpServerPrompt"/> that delegates all operations to an inner <see cref="McpServerPrompt"/>.</summary>
/// <remarks>
/// This is recommended as a base type when building prompts that can be chained around an underlying <see cref="McpServerPrompt"/>.
/// The default implementation simply passes each call to the inner prompt instance.
/// </remarks>
public abstract class DelegatingMcpServerPrompt : McpServerPrompt
{
    private readonly McpServerPrompt _innerPrompt;

    /// <summary>Initializes a new instance of the <see cref="DelegatingMcpServerPrompt"/> class around the specified <paramref name="innerPrompt"/>.</summary>
    /// <param name="innerPrompt">The inner prompt wrapped by this delegating prompt.</param>
    protected DelegatingMcpServerPrompt(McpServerPrompt innerPrompt)
    {
        Throw.IfNull(innerPrompt);
        _innerPrompt = innerPrompt;
    }

    /// <inheritdoc />
    public override Prompt ProtocolPrompt => _innerPrompt.ProtocolPrompt;

    /// <inheritdoc />
    public override Task<GetPromptResult> GetAsync(
        RequestContext<GetPromptRequestParams> request, 
        CancellationToken cancellationToken = default) =>
        _innerPrompt.GetAsync(request, cancellationToken);

    /// <inheritdoc />
    public override string ToString() => _innerPrompt.ToString();
}
