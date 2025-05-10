using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace ModelContextProtocol.AspNetCore.Tests;

public class MapMcpStatelessTests(ITestOutputHelper outputHelper) : MapMcpStreamableHttpTests(outputHelper)
{
    protected override bool UseStreamableHttp => true;
    protected override bool Stateless => true;
}
