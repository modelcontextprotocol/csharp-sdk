using System.IO;
using PublicApiGenerator;
using Xunit;

namespace ModelContextProtocol.AspNetCore.Tests;

public class PublicApiTests
{
    [Fact]
    public void AspNetCore_PublicApi_Approved()
    {
        var api = typeof(ModelContextProtocol.AspNetCore.HttpMcpServerBuilderExtensions).Assembly.GeneratePublicApi(new ApiGeneratorOptions { IncludeAssemblyAttributes = false });
        var approved = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "PublicAPI.ModelContextProtocol.AspNetCore.txt"));
        Assert.Equal(approved, api);
    }
}
