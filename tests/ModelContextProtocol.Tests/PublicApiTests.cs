using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using PublicApiGenerator;

namespace ModelContextProtocol.Tests;

public class PublicApiTests
{
    private static readonly ApiGeneratorOptions Options = new() { IncludeAssemblyAttributes = false };

    [Fact]
    public void ModelContextProtocol_PublicApi_Approved()
    {
        var api = typeof(IMcpServerBuilder).Assembly.GeneratePublicApi(Options);
        var approved = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "PublicApiTests.ModelContextProtocol.txt"));
        Assert.Equal(approved, api);
    }

    [Fact]
    public void ModelContextProtocolCore_PublicApi_Approved()
    {
        var api = typeof(IMcpClient).Assembly.GeneratePublicApi(Options);
        var approved = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "PublicApiTests.ModelContextProtocol.Core.txt"));
        Assert.Equal(approved, api);
    }
}
