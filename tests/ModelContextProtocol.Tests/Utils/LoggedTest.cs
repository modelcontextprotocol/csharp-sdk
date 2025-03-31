using Microsoft.Extensions.Logging;
using ModelContextProtocol.Test.Utils;

namespace ModelContextProtocol.Tests.Utils;

public class LoggedTest : IDisposable
{
    private readonly DelegatingTestOutputHelper _delegatingTestOutputHelper;

    public LoggedTest(ITestOutputHelper testOutputHelper)
    {
        _delegatingTestOutputHelper = new()
        {
            CurrentTestOutputHelper = testOutputHelper,
        };
        LoggerProvider = new XunitLoggerProvider(_delegatingTestOutputHelper);
        LoggerFactory = Microsoft.Extensions.Logging.LoggerFactory.Create(builder =>
        {
            builder.AddProvider(LoggerProvider);
        });
    }

    public ITestOutputHelper TestOutputHelper => _delegatingTestOutputHelper;
    public ILoggerFactory LoggerFactory { get; }
    public ILoggerProvider LoggerProvider { get; }

    public virtual void Dispose()
    {
        _delegatingTestOutputHelper.CurrentTestOutputHelper = null;
    }
}
