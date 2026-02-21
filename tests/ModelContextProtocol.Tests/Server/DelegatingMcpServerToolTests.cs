using ModelContextProtocol.Server;
using System.Reflection;

namespace ModelContextProtocol.Tests.Server;

public class DelegatingMcpServerToolTests
{
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
}
