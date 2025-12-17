using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using System.Collections.Immutable;
using System.Linq;

namespace ModelContextProtocol.Analyzers;

/// <summary>
/// Suppresses CS1066 warnings for MCP server methods that have optional parameters.
/// </summary>
/// <remarks>
/// <para>
/// CS1066 is issued when a partial method's implementing declaration has default parameter values.
/// For partial methods, only the defining declaration's defaults are used by callers,
/// making the implementing declaration's defaults redundant.
/// </para>
/// <para>
/// However, for MCP tool, prompt, and resource methods, users often want to specify default values
/// in their implementing declaration for documentation purposes. The XmlToDescriptionGenerator
/// automatically copies these defaults to the generated defining declaration, making them functional.
/// </para>
/// <para>
/// This suppressor suppresses CS1066 for methods marked with [McpServerTool], [McpServerPrompt],
/// or [McpServerResource] attributes, allowing users to specify defaults in their code without warnings.
/// </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class CS1066Suppressor : DiagnosticSuppressor
{
    private static readonly SuppressionDescriptor McpToolSuppression = new(
        id: "MCP_CS1066_TOOL",
        suppressedDiagnosticId: "CS1066",
        justification: "Default values on MCP tool method implementing declarations are copied to the generated defining declaration by the source generator.");

    private static readonly SuppressionDescriptor McpPromptSuppression = new(
        id: "MCP_CS1066_PROMPT",
        suppressedDiagnosticId: "CS1066",
        justification: "Default values on MCP prompt method implementing declarations are copied to the generated defining declaration by the source generator.");

    private static readonly SuppressionDescriptor McpResourceSuppression = new(
        id: "MCP_CS1066_RESOURCE",
        suppressedDiagnosticId: "CS1066",
        justification: "Default values on MCP resource method implementing declarations are copied to the generated defining declaration by the source generator.");

    /// <inheritdoc/>
    public override ImmutableArray<SuppressionDescriptor> SupportedSuppressions =>
        ImmutableArray.Create(McpToolSuppression, McpPromptSuppression, McpResourceSuppression);

    /// <inheritdoc/>
    public override void ReportSuppressions(SuppressionAnalysisContext context)
    {
        foreach (Diagnostic diagnostic in context.ReportedDiagnostics)
        {
            Location? location = diagnostic.Location;
            SyntaxTree? tree = location.SourceTree;
            if (tree is null)
            {
                continue;
            }

            SyntaxNode root = tree.GetRoot(context.CancellationToken);
            SyntaxNode? node = root.FindNode(location.SourceSpan);

            // Find the containing method declaration
            MethodDeclarationSyntax? method = node.FirstAncestorOrSelf<MethodDeclarationSyntax>();
            if (method is null)
            {
                continue;
            }

            // Check for MCP attributes
            SuppressionDescriptor? suppression = GetSuppressionForMethod(method, context);
            if (suppression is not null)
            {
                context.ReportSuppression(Suppression.Create(suppression, diagnostic));
            }
        }
    }

    private static SuppressionDescriptor? GetSuppressionForMethod(MethodDeclarationSyntax method, SuppressionAnalysisContext context)
    {
        SemanticModel? semanticModel = context.GetSemanticModel(method.SyntaxTree);
        IMethodSymbol? methodSymbol = semanticModel.GetDeclaredSymbol(method, context.CancellationToken);

        if (methodSymbol is null)
        {
            return null;
        }

        foreach (AttributeData attribute in methodSymbol.GetAttributes())
        {
            string? fullName = attribute.AttributeClass?.ToDisplayString();
            if (fullName is null)
            {
                continue;
            }

            if (fullName == "ModelContextProtocol.Server.McpServerToolAttribute")
            {
                return McpToolSuppression;
            }

            if (fullName == "ModelContextProtocol.Server.McpServerPromptAttribute")
            {
                return McpPromptSuppression;
            }

            if (fullName == "ModelContextProtocol.Server.McpServerResourceAttribute")
            {
                return McpResourceSuppression;
            }
        }

        return null;
    }
}
