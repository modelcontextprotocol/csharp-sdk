using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Immutable;
using System.Text;
using System.Xml.Linq;

namespace ModelContextProtocol.Analyzers;

/// <summary>Provides the diagnostic descriptors used by the assembly.</summary>
/// <remarks>
/// Analyzer diagnostic IDs are in the format MCP### (or MCP1### if ever needed).
/// <para>
/// Diagnostic IDs cannot be reused if an analyzer is removed.
/// This ensures that users do not suppress warnings for new diagnostics with existing
/// suppressions that might be left in place from prior uses of the same diagnostic ID.
/// </para>
/// </remarks>
internal static class Diagnostics
{
    public static DiagnosticDescriptor InvalidXmlDocumentation { get; } = new(
        id: "MCP001",
        title: "Invalid XML documentation for MCP method",
        messageFormat: "XML comment for method '{0}' is invalid and cannot be processed to generate [Description] attributes.",
        category: "mcp",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "The XML documentation comment contains invalid XML and cannot be processed to generate Description attributes.");

    public static DiagnosticDescriptor McpMethodMustBePartial { get; } = new(
        id: "MCP002",
        title: "MCP method must be partial to generate [Description] attributes",
        messageFormat: "Method '{0}' has XML documentation that could be used to generate [Description] attributes, but the method is not declared as partial.",
        category: "mcp",
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "Methods with MCP attributes should be declared as partial to allow the source generator to emit Description attributes from XML documentation comments.");
}
