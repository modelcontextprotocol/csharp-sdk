using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Moq;
using System.Reflection;

namespace ModelContextProtocol.Tests.Server;

public class DelegatingMcpServerPromptTests
{
    private static JsonRpcRequest CreateTestJsonRpcRequest() => new()
    {
        Id = new RequestId("test-id"),
        Method = "test/method",
        Params = null,
    };

    [Fact]
    public void Ctor_NullInnerPrompt_Throws()
    {
        Assert.Throws<ArgumentNullException>("innerPrompt", () => new TestDelegatingMcpServerPrompt(null!));
    }

    [Fact]
    public void ProtocolPrompt_DelegatesToInnerPrompt()
    {
        Prompt expected = new() { Name = "test-prompt" };
        Mock<McpServerPrompt> mock = new();
        mock.Setup(p => p.ProtocolPrompt).Returns(expected);

        TestDelegatingMcpServerPrompt delegating = new(mock.Object);

        Assert.Same(expected, delegating.ProtocolPrompt);
        mock.Verify(p => p.ProtocolPrompt, Times.Once);
    }

    [Fact]
    public void Metadata_DelegatesToInnerPrompt()
    {
        IReadOnlyList<object> expected = new object[] { "attr1", "attr2" };
        Mock<McpServerPrompt> mock = new();
        mock.Setup(p => p.Metadata).Returns(expected);

        TestDelegatingMcpServerPrompt delegating = new(mock.Object);

        Assert.Same(expected, delegating.Metadata);
        mock.Verify(p => p.Metadata, Times.Once);
    }

    [Fact]
    public async Task GetAsync_DelegatesToInnerPrompt()
    {
        GetPromptResult expected = new() { Messages = [] };
        RequestContext<GetPromptRequestParams> request = new(new Mock<McpServer>().Object, CreateTestJsonRpcRequest());
        using CancellationTokenSource cts = new();
        Mock<McpServerPrompt> mock = new();
        mock.Setup(p => p.GetAsync(request, cts.Token)).ReturnsAsync(expected);

        TestDelegatingMcpServerPrompt delegating = new(mock.Object);

        GetPromptResult result = await delegating.GetAsync(request, cts.Token);

        Assert.Same(expected, result);
        mock.Verify(p => p.GetAsync(request, cts.Token), Times.Once);
    }

    [Fact]
    public void ToString_DelegatesToInnerPrompt()
    {
        Mock<McpServerPrompt> mock = new();
        mock.Setup(p => p.ToString()).Returns("inner-prompt-string");

        TestDelegatingMcpServerPrompt delegating = new(mock.Object);

        Assert.Equal("inner-prompt-string", delegating.ToString());
    }

    [Fact]
    public void OverridesAllVirtualAndAbstractMembers()
    {
        MethodInfo[] baseMethods = typeof(McpServerPrompt).GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => (m.IsVirtual || m.IsAbstract) && m.DeclaringType == typeof(McpServerPrompt))
            .ToArray();

        Assert.NotEmpty(baseMethods);

        foreach (MethodInfo baseMethod in baseMethods)
        {
            MethodInfo? overriddenMethod = typeof(DelegatingMcpServerPrompt).GetMethod(
                baseMethod.Name,
                BindingFlags.Public | BindingFlags.Instance,
                baseMethod.GetParameters().Select(p => p.ParameterType).ToArray());

            Assert.True(
                overriddenMethod is not null && overriddenMethod.DeclaringType == typeof(DelegatingMcpServerPrompt),
                $"DelegatingMcpServerPrompt does not override {baseMethod.Name} from McpServerPrompt.");
        }
    }

    private sealed class TestDelegatingMcpServerPrompt(McpServerPrompt innerPrompt) : DelegatingMcpServerPrompt(innerPrompt);
}
