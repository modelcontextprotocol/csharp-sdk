using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace ModelContextProtocol.Tests.Utils;

public class MockLoggerProvider : ILoggerProvider
{
    public ConcurrentQueue<(string Category, LogLevel LogLevel, EventId EventId, string Message, Exception? Exception)> LogMessages { get; } = [];

    public ILogger CreateLogger(string categoryName)
    {
        return new MockLogger(this, categoryName);
    }

    public void Dispose()
    {
    }

    private class MockLogger(MockLoggerProvider mockProvider, string category) : ILogger
    {
        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            mockProvider.LogMessages.Enqueue((category, logLevel, eventId, formatter(state, exception), exception));
        }

        public bool IsEnabled(LogLevel logLevel) => true;

        // The MockLoggerProvider is a convenient NoopDisposable
        public IDisposable BeginScope<TState>(TState state) where TState : notnull => mockProvider;
    }
}
