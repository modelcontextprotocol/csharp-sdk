namespace Microsoft.Extensions.DependencyInjection;

internal sealed class DefaultMcpMessageFilterBuilder(IMcpServerBuilder serverBuilder) : IMcpMessageFilterBuilder
{
    // Test. Do not merge.
    public IServiceCollection Services { get; } = serverBuilder.Services;
}
