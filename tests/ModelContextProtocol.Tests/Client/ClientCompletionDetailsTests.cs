using ModelContextProtocol.Client;

namespace ModelContextProtocol.Tests.Client;

public class ClientCompletionDetailsTests
{
    [Fact]
    public void ClientCompletionDetails_PropertiesRoundtrip()
    {
        var exception = new InvalidOperationException("test");
        var details = new ClientCompletionDetails
        {
            Exception = exception,
        };

        Assert.Same(exception, details.Exception);
    }

    [Fact]
    public void ClientCompletionDetails_DefaultsToNull()
    {
        var details = new ClientCompletionDetails();
        Assert.Null(details.Exception);
    }

    [Fact]
    public void StdioClientCompletionDetails_PropertiesRoundtrip()
    {
        var exception = new IOException("process exited");
        string[] stderrLines = ["error line 1", "error line 2"];

        var details = new StdioClientCompletionDetails
        {
            Exception = exception,
            ProcessId = 12345,
            ExitCode = 42,
            StandardErrorTail = stderrLines,
        };

        Assert.Same(exception, details.Exception);
        Assert.Equal(12345, details.ProcessId);
        Assert.Equal(42, details.ExitCode);
        Assert.Same(stderrLines, details.StandardErrorTail);
    }

    [Fact]
    public void StdioClientCompletionDetails_DefaultsToNull()
    {
        var details = new StdioClientCompletionDetails();
        Assert.Null(details.Exception);
        Assert.Null(details.ProcessId);
        Assert.Null(details.ExitCode);
        Assert.Null(details.StandardErrorTail);
    }

    [Fact]
    public void StdioClientCompletionDetails_IsClientCompletionDetails()
    {
        ClientCompletionDetails details = new StdioClientCompletionDetails { ExitCode = 1 };
        var stdio = Assert.IsType<StdioClientCompletionDetails>(details);
        Assert.Equal(1, stdio.ExitCode);
    }

}
