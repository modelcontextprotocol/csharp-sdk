using ModelContextProtocol.Server;
using System.Reflection;

namespace ModelContextProtocol.Tests.Server;

public class DelegatingMcpServerPrimitiveTests
{
    [Theory]
    [InlineData(typeof(DelegatingMcpServerTool), typeof(McpServerTool))]
    [InlineData(typeof(DelegatingMcpServerPrompt), typeof(McpServerPrompt))]
    [InlineData(typeof(DelegatingMcpServerResource), typeof(McpServerResource))]
    public void DelegatingType_OverridesAllVirtualAndAbstractMembers(Type delegatingType, Type baseType)
    {
        // Get all public instance methods on the base type that are virtual/abstract
        // (this includes property getters/setters) and declared on the base type itself,
        // excluding those inherited from System.Object.
        MethodInfo[] baseMethods = baseType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => (m.IsVirtual || m.IsAbstract) && m.DeclaringType == baseType)
            .ToArray();

        Assert.NotEmpty(baseMethods);

        foreach (MethodInfo baseMethod in baseMethods)
        {
            // Find the corresponding method on the delegating type
            MethodInfo? overriddenMethod = delegatingType.GetMethod(
                baseMethod.Name,
                BindingFlags.Public | BindingFlags.Instance,
                baseMethod.GetParameters().Select(p => p.ParameterType).ToArray());

            Assert.True(
                overriddenMethod is not null && overriddenMethod.DeclaringType == delegatingType,
                $"{delegatingType.Name} does not override {baseMethod.Name} from {baseType.Name}.");
        }
    }
}
