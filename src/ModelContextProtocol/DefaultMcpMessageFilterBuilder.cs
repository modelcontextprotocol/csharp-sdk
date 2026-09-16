namespace Microsoft.Extensions.DependencyInjection;

internal sealed class DefaultMcpMessageFilterBuilder(IMcpServerBuilder serverBuilder) : IMcpMessageFilterBuilder
{
    public IServiceCollection Services { get; } = serverBuilder.Services;
}
