using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.Reflection;

namespace ModelContextProtocol.Tests.Server;

public class DelegatingMcpServerToolTests
{
    [Fact]
    public void Ctor_NullInnerTool_Throws()
    {
        Assert.Throws<ArgumentNullException>("innerTool", () => new TestDelegatingTool(null!));
    }

    [Fact]
    public async Task AllMembers_DelegateToInnerTool()
    {
        Tool expectedTool = new() { Name = "sentinel-tool" };
        IReadOnlyList<object> expectedMetadata = new object[] { "m1" };
        CallToolResult expectedResult = new() { Content = [] };
        InnerTool inner = new(expectedTool, expectedMetadata, expectedResult);

        TestDelegatingTool delegating = new(inner);

        Assert.Same(expectedTool, delegating.ProtocolTool);
        Assert.Same(expectedMetadata, delegating.Metadata);
        Assert.Same(expectedResult, await delegating.InvokeAsync(null!, CancellationToken.None));
        Assert.Equal(inner.ToString(), delegating.ToString());
    }

    [Fact]
    public void OverridesAllVirtualAndAbstractMembers()
    {
        MethodInfo[] baseMethods = typeof(McpServerTool).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => m.IsVirtual || m.IsAbstract)
            .ToArray();

        Assert.NotEmpty(baseMethods);

        foreach (MethodInfo baseMethod in baseMethods)
        {
            Assert.True(
                typeof(DelegatingMcpServerTool).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                    .Any(m => m.Name == baseMethod.Name),
                $"DelegatingMcpServerTool does not override {baseMethod.Name} from McpServerTool.");
        }
    }

    private sealed class TestDelegatingTool(McpServerTool innerTool) : DelegatingMcpServerTool(innerTool);

    private sealed class InnerTool(Tool protocolTool, IReadOnlyList<object> metadata, CallToolResult result) : McpServerTool
    {
        public override Tool ProtocolTool => protocolTool;
        public override IReadOnlyList<object> Metadata => metadata;
        public override ValueTask<CallToolResult> InvokeAsync(RequestContext<CallToolRequestParams> request, CancellationToken cancellationToken = default) => new(result);
        public override string ToString() => "inner-tool";
    }
}
