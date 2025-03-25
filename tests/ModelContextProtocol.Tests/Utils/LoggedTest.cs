using Microsoft.Extensions.Logging;
using ModelContextProtocol.Test.Utils;

namespace ModelContextProtocol.Tests.Utils;

public class LoggedTest(ITestOutputHelper testOutputHelper)
{
    public ITestOutputHelper TestOutputHelper { get; } = testOutputHelper;
    public ILoggerFactory LoggerFactory { get; } = CreateLoggerFactory(testOutputHelper);

    private static ILoggerFactory CreateLoggerFactory(ITestOutputHelper testOutputHelper)
    {
        return Microsoft.Extensions.Logging.LoggerFactory.Create(builder =>
        {
            builder.AddProvider(new XunitLoggerProvider(testOutputHelper));
        });
    }
}
