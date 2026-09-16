using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.Reflection;

namespace ModelContextProtocol.Tests.Server;

public class DelegatingMcpServerResourceTests
{
    [Fact]
    public void Ctor_NullInnerResource_Throws()
    {
        Assert.Throws<ArgumentNullException>("innerResource", () => new TestDelegatingResource(null!));
    }

    [Fact]
    public async Task AllMembers_DelegateToInnerResource()
    {
        ResourceTemplate expectedTemplate = new() { Name = "sentinel-resource", UriTemplate = "test://resource" };
        Resource expectedResource = new() { Name = "sentinel-resource", Uri = "test://resource" };
        IReadOnlyList<object> expectedMetadata = new object[] { "m1" };
        ReadResourceResult expectedResult = new() { Contents = [] };
        InnerResource inner = new(expectedTemplate, expectedResource, expectedMetadata, expectedResult);

        TestDelegatingResource delegating = new(inner);

        Assert.Same(expectedTemplate, delegating.ProtocolResourceTemplate);
        Assert.Same(expectedResource, delegating.ProtocolResource);
        Assert.Same(expectedMetadata, delegating.Metadata);
        Assert.True(delegating.IsMatch("test://resource"));
        Assert.Same(expectedResult, await delegating.ReadAsync(null!, CancellationToken.None));
        Assert.Equal(inner.ToString(), delegating.ToString());
    }

    [Fact]
    public void OverridesAllVirtualAndAbstractMembers()
    {
        MethodInfo[] baseMethods = typeof(McpServerResource).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => m.IsVirtual || m.IsAbstract)
            .ToArray();

        Assert.NotEmpty(baseMethods);

        foreach (MethodInfo baseMethod in baseMethods)
        {
            Assert.True(
                typeof(DelegatingMcpServerResource).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                    .Any(m => m.Name == baseMethod.Name),
                $"DelegatingMcpServerResource does not override {baseMethod.Name} from McpServerResource.");
        }
    }

    private sealed class TestDelegatingResource(McpServerResource innerResource) : DelegatingMcpServerResource(innerResource);

    private sealed class InnerResource(ResourceTemplate protocolResourceTemplate, Resource protocolResource, IReadOnlyList<object> metadata, ReadResourceResult result) : McpServerResource
    {
        public override ResourceTemplate ProtocolResourceTemplate => protocolResourceTemplate;
        public override Resource? ProtocolResource => protocolResource;
        public override IReadOnlyList<object> Metadata => metadata;
        public override bool IsMatch(string uri) => true;
        public override ValueTask<ReadResourceResult> ReadAsync(RequestContext<ReadResourceRequestParams> request, CancellationToken cancellationToken = default) => new(result);
        public override string ToString() => "inner-resource";
    }
}
