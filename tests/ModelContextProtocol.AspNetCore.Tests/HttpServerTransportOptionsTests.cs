namespace ModelContextProtocol.AspNetCore.Tests;

public class HttpServerTransportOptionsTests
{
    [Fact]
    public void SessionMode_DefaultsToStateless()
    {
        Assert.Equal(HttpServerSessionMode.Stateless, new HttpServerTransportOptions().SessionMode);
    }

    [Theory]
    [InlineData(true, HttpServerSessionMode.Stateless)]
    [InlineData(false, HttpServerSessionMode.Stateful)]
    public void SettingStateless_SelectsEquivalentSessionMode(bool stateless, HttpServerSessionMode expected)
    {
        var options = new HttpServerTransportOptions { Stateless = stateless };
        Assert.Equal(expected, options.SessionMode);
    }

    [Theory]
    [InlineData(HttpServerSessionMode.Stateless, true)]
    [InlineData(HttpServerSessionMode.Stateful, false)]
    [InlineData(HttpServerSessionMode.StatefulForInitializeClients, false)]
    public void ReadingStateless_ReflectsSessionMode(HttpServerSessionMode sessionMode, bool expected)
    {
        var options = new HttpServerTransportOptions { SessionMode = sessionMode };
        Assert.Equal(expected, options.Stateless);
    }

    [Fact]
    public void AssigningBothProperties_DoesNotThrow_AndLastAssignmentWins()
    {
        var options = new HttpServerTransportOptions();

        options.Stateless = false;
        options.SessionMode = HttpServerSessionMode.StatefulForInitializeClients;
        Assert.Equal(HttpServerSessionMode.StatefulForInitializeClients, options.SessionMode);

        options.SessionMode = HttpServerSessionMode.StatefulForInitializeClients;
        options.Stateless = true;
        Assert.Equal(HttpServerSessionMode.Stateless, options.SessionMode);
    }
}
