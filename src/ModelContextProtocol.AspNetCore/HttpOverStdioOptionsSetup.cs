using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Options;

namespace ModelContextProtocol.AspNetCore;

internal sealed class HttpOverStdioKestrelOptionsSetup : IConfigureOptions<KestrelServerOptions>
{
    public void Configure(KestrelServerOptions options)
    {
        options.Listen(StdioEndPoint.Instance, listenOptions =>
        {
            listenOptions.Protocols = HttpProtocols.Http2;
        });
    }
}

internal sealed class HttpOverStdioConsoleLoggerOptionsSetup : IConfigureOptions<ConsoleLoggerOptions>
{
    public void Configure(ConsoleLoggerOptions options)
    {
        options.LogToStandardErrorThreshold = LogLevel.Trace;
    }
}
