using System.IO;
using PublicApiGenerator;
using Xunit;

namespace ModelContextProtocol.Tests;

public class PublicApiTests
{
    [Fact]
    public void ModelContextProtocol_PublicApi_Approved()
    {
        var api = typeof(ModelContextProtocol.McpServerHandlers).Assembly.GeneratePublicApi(new ApiGeneratorOptions { IncludeAssemblyAttributes = false });
        var approved = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "PublicAPI.ModelContextProtocol.txt"));
        Assert.Equal(approved, api);
    }

    [Fact]
    public void ModelContextProtocolCore_PublicApi_Approved()
    {
        var api = typeof(ModelContextProtocol.McpEndpoint).Assembly.GeneratePublicApi(new ApiGeneratorOptions { IncludeAssemblyAttributes = false });
        var approved = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "PublicAPI.ModelContextProtocol.Core.txt"));
        Assert.Equal(approved, api);
    }
}
