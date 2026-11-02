namespace ModelContextProtocol.AspNetCore.Tests;

public class HttpServerTransportOptionsTests
{
    [Fact]
    public void SessionMode_DefaultsToStateless()
    {
        var options = new HttpServerTransportOptions();

        Assert.Equal(HttpServerSessionMode.Stateless, options.SessionMode);
        Assert.True(options.Stateless);
    }

    [Theory]
    [InlineData(true, HttpServerSessionMode.Stateless)]
    [InlineData(false, HttpServerSessionMode.Stateful)]
    public void SettingStateless_SelectsEquivalentSessionMode(bool stateless, HttpServerSessionMode expected)
    {
        var options = new HttpServerTransportOptions { Stateless = stateless };

        Assert.Equal(stateless, options.Stateless);
        Assert.Equal(expected, options.SessionMode);
    }

    [Theory]
    [InlineData(HttpServerSessionMode.Stateless, true)]
    [InlineData(HttpServerSessionMode.Stateful, false)]
    public void ReadingStateless_ReflectsSessionMode(HttpServerSessionMode sessionMode, bool expected)
    {
        var options = new HttpServerTransportOptions { SessionMode = sessionMode };

        Assert.Equal(expected, options.Stateless);
    }

    [Fact]
    public void ReadingStateless_ReturnsFalseForHybridMode()
    {
        var options = new HttpServerTransportOptions
        {
            SessionMode = HttpServerSessionMode.StatefulForInitializeClients,
        };

        Assert.False(options.Stateless);
        Assert.Equal(HttpServerSessionMode.StatefulForInitializeClients, options.SessionMode);
    }

    [Fact]
    public void AssigningBothProperties_DoesNotThrow_AndLastAssignmentWins()
    {
        var options = new HttpServerTransportOptions();

        options.Stateless = false;
        Assert.False(options.Stateless);
        Assert.Equal(HttpServerSessionMode.Stateful, options.SessionMode);

        options.SessionMode = HttpServerSessionMode.StatefulForInitializeClients;
        Assert.False(options.Stateless);
        Assert.Equal(HttpServerSessionMode.StatefulForInitializeClients, options.SessionMode);

        options.Stateless = true;
        Assert.True(options.Stateless);
        Assert.Equal(HttpServerSessionMode.Stateless, options.SessionMode);

        options.SessionMode = HttpServerSessionMode.Stateful;
        Assert.False(options.Stateless);
        Assert.Equal(HttpServerSessionMode.Stateful, options.SessionMode);
    }
}
