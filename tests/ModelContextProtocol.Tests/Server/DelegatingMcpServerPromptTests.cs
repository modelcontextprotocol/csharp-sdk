using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.Reflection;

namespace ModelContextProtocol.Tests.Server;

public class DelegatingMcpServerPromptTests
{
    [Fact]
    public void Ctor_NullInnerPrompt_Throws()
    {
        Assert.Throws<ArgumentNullException>("innerPrompt", () => new TestDelegatingPrompt(null!));
    }

    [Fact]
    public async Task AllMembers_DelegateToInnerPrompt()
    {
        Prompt expectedPrompt = new() { Name = "sentinel-prompt" };
        IReadOnlyList<object> expectedMetadata = new object[] { "m1" };
        GetPromptResult expectedResult = new() { Messages = [] };
        InnerPrompt inner = new(expectedPrompt, expectedMetadata, expectedResult);

        TestDelegatingPrompt delegating = new(inner);

        Assert.Same(expectedPrompt, delegating.ProtocolPrompt);
        Assert.Same(expectedMetadata, delegating.Metadata);
        Assert.Same(expectedResult, await delegating.GetAsync(null!, CancellationToken.None));
        Assert.Equal(inner.ToString(), delegating.ToString());
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
                baseMethod.GetParameters().Select(p => p.ParameterType).ToArray());

            Assert.True(
                overriddenMethod is not null && overriddenMethod.DeclaringType == typeof(DelegatingMcpServerPrompt),
                $"DelegatingMcpServerPrompt does not override {baseMethod.Name} from McpServerPrompt.");
        }
    }

    private sealed class TestDelegatingPrompt(McpServerPrompt innerPrompt) : DelegatingMcpServerPrompt(innerPrompt);

    private sealed class InnerPrompt(Prompt protocolPrompt, IReadOnlyList<object> metadata, GetPromptResult result) : McpServerPrompt
    {
        public override Prompt ProtocolPrompt => protocolPrompt;
        public override IReadOnlyList<object> Metadata => metadata;
        public override ValueTask<GetPromptResult> GetAsync(RequestContext<GetPromptRequestParams> request, CancellationToken cancellationToken = default) => new(result);
        public override string ToString() => "inner-prompt";
    }
}
