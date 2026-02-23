using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Server;

namespace ModelContextProtocol.AspNetCore;

/// <summary>
/// Post-configures <see cref="HttpServerTransportOptions"/> by resolving services from DI
/// when they haven't been explicitly set on the options.
/// </summary>
internal sealed class HttpServerTransportOptionsSetup(IServiceProvider serviceProvider) : IConfigureOptions<HttpServerTransportOptions>
{
    public void Configure(HttpServerTransportOptions options)
    {
        options.EventStreamStore ??= serviceProvider.GetService<ISseEventStreamStore>();
        options.SessionMigrationHandler ??= serviceProvider.GetService<ISessionMigrationHandler>();
    }
}
