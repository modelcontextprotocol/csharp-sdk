using Microsoft.AspNetCore.Builder;
using PublicApiGenerator;

namespace ModelContextProtocol.AspNetCore.Tests;

public class PublicApiTests
{
    [Fact]
    public void AspNetCore_PublicApi_Approved()
    {
        var api = typeof(McpEndpointRouteBuilderExtensions).Assembly.GeneratePublicApi(new ApiGeneratorOptions { IncludeAssemblyAttributes = false });
        var approved = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "PublicApiTests.ModelContextProtocol.AspNetCore.txt"));
        Assert.Equal(approved, api);
    }
}
