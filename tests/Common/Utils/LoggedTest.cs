using Microsoft.Extensions.Logging;

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
        XunitLoggerProvider = new XunitLoggerProvider(_delegatingTestOutputHelper);
        LoggerFactory = Microsoft.Extensions.Logging.LoggerFactory.Create(builder =>
        {
            builder.AddProvider(XunitLoggerProvider);
            builder.AddProvider(MockLoggerProvider);
            builder.SetMinimumLevel(LogLevel.Debug);
        });
    }

    public ITestOutputHelper TestOutputHelper => _delegatingTestOutputHelper;
    public ILoggerFactory LoggerFactory { get; set; }
    public ILoggerProvider XunitLoggerProvider { get; }
    public MockLoggerProvider MockLoggerProvider { get; } = new();

    public virtual void Dispose()
    {
        _delegatingTestOutputHelper.CurrentTestOutputHelper = null;
    }
}
