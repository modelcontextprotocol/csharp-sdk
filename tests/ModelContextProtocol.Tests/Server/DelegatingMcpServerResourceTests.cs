using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Moq;
using System.Reflection;

namespace ModelContextProtocol.Tests.Server;

public class DelegatingMcpServerResourceTests
{
    private static JsonRpcRequest CreateTestJsonRpcRequest() => new()
    {
        Id = new RequestId("test-id"),
        Method = "test/method",
        Params = null,
    };

    [Fact]
    public void Ctor_NullInnerResource_Throws()
    {
        Assert.Throws<ArgumentNullException>("innerResource", () => new TestDelegatingMcpServerResource(null!));
    }

    [Fact]
    public void ProtocolResourceTemplate_DelegatesToInnerResource()
    {
        ResourceTemplate expected = new() { Name = "test-resource", UriTemplate = "test://resource" };
        Mock<McpServerResource> mock = new();
        mock.Setup(r => r.ProtocolResourceTemplate).Returns(expected);

        TestDelegatingMcpServerResource delegating = new(mock.Object);

        Assert.Same(expected, delegating.ProtocolResourceTemplate);
        mock.Verify(r => r.ProtocolResourceTemplate, Times.Once);
    }

    [Fact]
    public void ProtocolResource_DelegatesToInnerResource()
    {
        Resource expected = new() { Name = "test-resource", Uri = "test://resource" };
        Mock<McpServerResource> mock = new();
        mock.Setup(r => r.ProtocolResource).Returns(expected);

        TestDelegatingMcpServerResource delegating = new(mock.Object);

        Assert.Same(expected, delegating.ProtocolResource);
        mock.Verify(r => r.ProtocolResource, Times.Once);
    }

    [Fact]
    public void Metadata_DelegatesToInnerResource()
    {
        IReadOnlyList<object> expected = new object[] { "attr1", "attr2" };
        Mock<McpServerResource> mock = new();
        mock.Setup(r => r.Metadata).Returns(expected);

        TestDelegatingMcpServerResource delegating = new(mock.Object);

        Assert.Same(expected, delegating.Metadata);
        mock.Verify(r => r.Metadata, Times.Once);
    }

    [Fact]
    public void IsMatch_DelegatesToInnerResource()
    {
        Mock<McpServerResource> mock = new();
        mock.Setup(r => r.IsMatch("test://resource")).Returns(true);

        TestDelegatingMcpServerResource delegating = new(mock.Object);

        Assert.True(delegating.IsMatch("test://resource"));
        mock.Verify(r => r.IsMatch("test://resource"), Times.Once);
    }

    [Fact]
    public async Task ReadAsync_DelegatesToInnerResource()
    {
        ReadResourceResult expected = new() { Contents = [] };
        RequestContext<ReadResourceRequestParams> request = new(new Mock<McpServer>().Object, CreateTestJsonRpcRequest());
        using CancellationTokenSource cts = new();
        Mock<McpServerResource> mock = new();
        mock.Setup(r => r.ReadAsync(request, cts.Token)).ReturnsAsync(expected);

        TestDelegatingMcpServerResource delegating = new(mock.Object);

        ReadResourceResult result = await delegating.ReadAsync(request, cts.Token);

        Assert.Same(expected, result);
        mock.Verify(r => r.ReadAsync(request, cts.Token), Times.Once);
    }

    [Fact]
    public void ToString_DelegatesToInnerResource()
    {
        Mock<McpServerResource> mock = new();
        mock.Setup(r => r.ToString()).Returns("inner-resource-string");

        TestDelegatingMcpServerResource delegating = new(mock.Object);

        Assert.Equal("inner-resource-string", delegating.ToString());
    }

    [Fact]
    public void OverridesAllVirtualAndAbstractMembers()
    {
        MethodInfo[] baseMethods = typeof(McpServerResource).GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => (m.IsVirtual || m.IsAbstract) && m.DeclaringType == typeof(McpServerResource))
            .ToArray();

        Assert.NotEmpty(baseMethods);

        foreach (MethodInfo baseMethod in baseMethods)
        {
            MethodInfo? overriddenMethod = typeof(DelegatingMcpServerResource).GetMethod(
                baseMethod.Name,
                BindingFlags.Public | BindingFlags.Instance,
                baseMethod.GetParameters().Select(p => p.ParameterType).ToArray());

            Assert.True(
                overriddenMethod is not null && overriddenMethod.DeclaringType == typeof(DelegatingMcpServerResource),
                $"DelegatingMcpServerResource does not override {baseMethod.Name} from McpServerResource.");
        }
    }

    private sealed class TestDelegatingMcpServerResource(McpServerResource innerResource) : DelegatingMcpServerResource(innerResource);
}
