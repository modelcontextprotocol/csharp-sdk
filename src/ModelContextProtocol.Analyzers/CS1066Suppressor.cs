using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;

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
        // Cache semantic models and attribute symbols per syntax tree/compilation to avoid redundant calls
        Dictionary<SyntaxTree, SemanticModel>? semanticModelCache = null;
        INamedTypeSymbol? mcpToolAttribute = null;
        INamedTypeSymbol? mcpPromptAttribute = null;
        INamedTypeSymbol? mcpResourceAttribute = null;
        bool attributesResolved = false;

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

            // Get or cache the semantic model for this tree
            semanticModelCache ??= new Dictionary<SyntaxTree, SemanticModel>();
            if (!semanticModelCache.TryGetValue(tree, out SemanticModel? semanticModel))
            {
                semanticModel = context.GetSemanticModel(tree);
                semanticModelCache[tree] = semanticModel;
            }

            // Resolve attribute symbols once per compilation
            if (!attributesResolved)
            {
                mcpToolAttribute = semanticModel.Compilation.GetTypeByMetadataName(McpAttributeNames.McpServerToolAttribute);
                mcpPromptAttribute = semanticModel.Compilation.GetTypeByMetadataName(McpAttributeNames.McpServerPromptAttribute);
                mcpResourceAttribute = semanticModel.Compilation.GetTypeByMetadataName(McpAttributeNames.McpServerResourceAttribute);
                attributesResolved = true;
            }

            // Check for MCP attributes
            SuppressionDescriptor? suppression = GetSuppressionForMethod(method, semanticModel, mcpToolAttribute, mcpPromptAttribute, mcpResourceAttribute, context.CancellationToken);
            if (suppression is not null)
            {
                context.ReportSuppression(Suppression.Create(suppression, diagnostic));
            }
        }
    }

    private static SuppressionDescriptor? GetSuppressionForMethod(
        MethodDeclarationSyntax method,
        SemanticModel semanticModel,
        INamedTypeSymbol? mcpToolAttribute,
        INamedTypeSymbol? mcpPromptAttribute,
        INamedTypeSymbol? mcpResourceAttribute,
        CancellationToken cancellationToken)
    {
        IMethodSymbol? methodSymbol = semanticModel.GetDeclaredSymbol(method, cancellationToken);

        if (methodSymbol is null)
        {
            return null;
        }

        foreach (AttributeData attribute in methodSymbol.GetAttributes())
        {
            INamedTypeSymbol? attributeClass = attribute.AttributeClass;
            if (attributeClass is null)
            {
                continue;
            }

            if (mcpToolAttribute is not null && SymbolEqualityComparer.Default.Equals(attributeClass, mcpToolAttribute))
            {
                return McpToolSuppression;
            }

            if (mcpPromptAttribute is not null && SymbolEqualityComparer.Default.Equals(attributeClass, mcpPromptAttribute))
            {
                return McpPromptSuppression;
            }

            if (mcpResourceAttribute is not null && SymbolEqualityComparer.Default.Equals(attributeClass, mcpResourceAttribute))
            {
                return McpResourceSuppression;
            }
        }

        return null;
    }
}
