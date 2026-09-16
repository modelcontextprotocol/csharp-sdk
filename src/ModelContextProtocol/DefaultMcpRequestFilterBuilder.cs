namespace Microsoft.Extensions.DependencyInjection;

internal sealed class DefaultMcpRequestFilterBuilder(IMcpServerBuilder serverBuilder) : IMcpRequestFilterBuilder
{
    public IServiceCollection Services { get; } = serverBuilder.Services;
}
