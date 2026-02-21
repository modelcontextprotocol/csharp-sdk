using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Moq;
using System.Reflection;

namespace ModelContextProtocol.Tests.Server;

public class DelegatingMcpServerToolTests
{
    private static JsonRpcRequest CreateTestJsonRpcRequest() => new()
    {
        Id = new RequestId("test-id"),
        Method = "test/method",
        Params = null,
    };

    [Fact]
    public void Ctor_NullInnerTool_Throws()
    {
        Assert.Throws<ArgumentNullException>("innerTool", () => new TestDelegatingMcpServerTool(null!));
    }

    [Fact]
    public void ProtocolTool_DelegatesToInnerTool()
    {
        Tool expected = new() { Name = "test-tool" };
        Mock<McpServerTool> mock = new();
        mock.Setup(t => t.ProtocolTool).Returns(expected);

        TestDelegatingMcpServerTool delegating = new(mock.Object);

        Assert.Same(expected, delegating.ProtocolTool);
        mock.Verify(t => t.ProtocolTool, Times.Once);
    }

    [Fact]
    public void Metadata_DelegatesToInnerTool()
    {
        IReadOnlyList<object> expected = new object[] { "attr1", "attr2" };
        Mock<McpServerTool> mock = new();
        mock.Setup(t => t.Metadata).Returns(expected);

        TestDelegatingMcpServerTool delegating = new(mock.Object);

        Assert.Same(expected, delegating.Metadata);
        mock.Verify(t => t.Metadata, Times.Once);
    }

    [Fact]
    public async Task InvokeAsync_DelegatesToInnerTool()
    {
        CallToolResult expected = new() { Content = [] };
        RequestContext<CallToolRequestParams> request = new(new Mock<McpServer>().Object, CreateTestJsonRpcRequest());
        using CancellationTokenSource cts = new();
        Mock<McpServerTool> mock = new();
        mock.Setup(t => t.InvokeAsync(request, cts.Token)).ReturnsAsync(expected);

        TestDelegatingMcpServerTool delegating = new(mock.Object);

        CallToolResult result = await delegating.InvokeAsync(request, cts.Token);

        Assert.Same(expected, result);
        mock.Verify(t => t.InvokeAsync(request, cts.Token), Times.Once);
    }

    [Fact]
    public void ToString_DelegatesToInnerTool()
    {
        Mock<McpServerTool> mock = new();
        mock.Setup(t => t.ToString()).Returns("inner-tool-string");

        TestDelegatingMcpServerTool delegating = new(mock.Object);

        Assert.Equal("inner-tool-string", delegating.ToString());
    }

    [Fact]
    public void OverridesAllVirtualAndAbstractMembers()
    {
        MethodInfo[] baseMethods = typeof(McpServerTool).GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => (m.IsVirtual || m.IsAbstract) && m.DeclaringType == typeof(McpServerTool))
            .ToArray();

        Assert.NotEmpty(baseMethods);

        foreach (MethodInfo baseMethod in baseMethods)
        {
            MethodInfo? overriddenMethod = typeof(DelegatingMcpServerTool).GetMethod(
                baseMethod.Name,
                BindingFlags.Public | BindingFlags.Instance,
                baseMethod.GetParameters().Select(p => p.ParameterType).ToArray());

            Assert.True(
                overriddenMethod is not null && overriddenMethod.DeclaringType == typeof(DelegatingMcpServerTool),
                $"DelegatingMcpServerTool does not override {baseMethod.Name} from McpServerTool.");
        }
    }

    private sealed class TestDelegatingMcpServerTool(McpServerTool innerTool) : DelegatingMcpServerTool(innerTool);
}
