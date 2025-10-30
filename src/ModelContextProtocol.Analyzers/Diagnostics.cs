using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Immutable;
using System.Text;
using System.Xml.Linq;

namespace ModelContextProtocol.Analyzers;

/// <summary>Provides the diagnostic descriptors used by the assembly.</summary>
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
}
