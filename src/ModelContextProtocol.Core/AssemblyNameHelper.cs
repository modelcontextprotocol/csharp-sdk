using System.Reflection;

namespace ModelContextProtocol;

internal static class AssemblyNameHelper
{
    /// <summary>Cached naming information used for MCP session name/version when none is specified.</summary>
    public static AssemblyName DefaultAssemblyName { get; } = (Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly()).GetName();
}
