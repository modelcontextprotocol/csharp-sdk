namespace ModelContextProtocol.Tests.Utils;

public class DelegatingTestOutputHelper() : ITestOutputHelper
{
    public ITestOutputHelper? CurrentTestOutputHelper { get; set; }

    public string Output => CurrentTestOutputHelper?.Output ?? string.Empty;

    public void Write(string message) => CurrentTestOutputHelper?.Write(message);
    public void Write(string format, params object[] args) => CurrentTestOutputHelper?.Write(format, args);
    public void WriteLine(string message) => CurrentTestOutputHelper?.WriteLine(message);
    public void WriteLine(string format, params object[] args) => CurrentTestOutputHelper?.WriteLine(format, args);
}
