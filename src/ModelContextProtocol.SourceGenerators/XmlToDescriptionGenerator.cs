using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading;
using System.Xml.Linq;

namespace ModelContextProtocol.SourceGenerators;

/// <summary>
/// Source generator that creates Description attributes from XML comments
/// for partial methods tagged with MCP attributes.
/// </summary>
[Generator]
public class XmlToDescriptionGenerator : IIncrementalGenerator
{
    private const string GeneratedFileName = "ModelContextProtocol.XmlComments.g.cs";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // Filter method declarations with attributes and transform to model
        var methodModels = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (s, _) => IsCandidateMethod(s),
                transform: static (ctx, ct) => GetMethodModel(ctx, ct))
            .Where(static m => m is not null);

        // Combine with compilation to get well-known type symbols
        var compilationAndMethods = context.CompilationProvider.Combine(methodModels.Collect());

        context.RegisterSourceOutput(compilationAndMethods,
            static (spc, source) => Execute(source.Left, source.Right!, spc));
    }

    private static bool IsCandidateMethod(SyntaxNode node)
    {
        // Quick syntax-only filter
        return node is MethodDeclarationSyntax 
        { 
            AttributeLists.Count: > 0 
        } method && method.Modifiers.Any(SyntaxKind.PartialKeyword);
    }

    private static MethodToGenerate? GetMethodModel(GeneratorSyntaxContext context, CancellationToken cancellationToken)
    {
        var methodDeclaration = (MethodDeclarationSyntax)context.Node;
        
        var methodSymbol = context.SemanticModel.GetDeclaredSymbol(methodDeclaration, cancellationToken);
        if (methodSymbol is null)
        {
            return null;
        }

        return new MethodToGenerate(methodDeclaration, methodSymbol);
    }

    private static void Execute(Compilation compilation, ImmutableArray<MethodToGenerate?> methods, SourceProductionContext context)
    {
        if (methods.IsDefaultOrEmpty)
        {
            return;
        }

        // Get well-known type symbols upfront
        var mcpServerToolAttribute = compilation.GetTypeByMetadataName("ModelContextProtocol.Server.McpServerToolAttribute");
        var mcpServerPromptAttribute = compilation.GetTypeByMetadataName("ModelContextProtocol.Server.McpServerPromptAttribute");
        var mcpServerResourceAttribute = compilation.GetTypeByMetadataName("ModelContextProtocol.Server.McpServerResourceAttribute");
        var descriptionAttribute = compilation.GetTypeByMetadataName("System.ComponentModel.DescriptionAttribute");

        if (descriptionAttribute is null)
        {
            // Description attribute is required - can't generate without it
            return;
        }

        if (mcpServerToolAttribute is null && mcpServerPromptAttribute is null && mcpServerResourceAttribute is null)
        {
            // No MCP attributes found - nothing to generate
            return;
        }

        var methodsToGenerate = new List<(IMethodSymbol MethodSymbol, MethodDeclarationSyntax MethodDeclaration, XmlDocumentation XmlDocs)>();

        foreach (var methodModel in methods)
        {
            if (methodModel is null)
            {
                continue;
            }

            var methodSymbol = methodModel.Value.MethodSymbol;
            var methodDeclaration = methodModel.Value.MethodDeclaration;

            // Check if method has any MCP attribute with symbol comparison
            var hasMcpAttribute = 
                (mcpServerToolAttribute is not null && HasAttribute(methodSymbol, mcpServerToolAttribute)) ||
                (mcpServerPromptAttribute is not null && HasAttribute(methodSymbol, mcpServerPromptAttribute)) ||
                (mcpServerResourceAttribute is not null && HasAttribute(methodSymbol, mcpServerResourceAttribute));

            if (!hasMcpAttribute)
            {
                continue;
            }

            // Extract XML documentation
            var xmlDocs = ExtractXmlDocumentation(methodSymbol);
            if (xmlDocs is null)
            {
                continue;
            }

            // Check if we need to generate anything for this method
            if (!NeedsGeneration(methodSymbol, xmlDocs, descriptionAttribute))
            {
                continue;
            }

            methodsToGenerate.Add((methodSymbol, methodDeclaration, xmlDocs));
        }

        if (methodsToGenerate.Count == 0)
        {
            return;
        }

        // Generate a single file with all partial declarations
        var source = GenerateSourceFile(methodsToGenerate, descriptionAttribute);
        context.AddSource(GeneratedFileName, SourceText.From(source, Encoding.UTF8));
    }

    private static bool HasAttribute(ISymbol symbol, INamedTypeSymbol attributeType)
    {
        return symbol.GetAttributes().Any(attr => 
            SymbolEqualityComparer.Default.Equals(attr.AttributeClass, attributeType));
    }

    private static bool NeedsGeneration(IMethodSymbol methodSymbol, XmlDocumentation xmlDocs, INamedTypeSymbol descriptionAttribute)
    {
        // Check if method needs Description attribute
        var needsMethodDescription = !string.IsNullOrWhiteSpace(xmlDocs.MethodDescription) && 
                                      !HasAttribute(methodSymbol, descriptionAttribute);

        // Check if return needs Description attribute
        var needsReturnDescription = !string.IsNullOrWhiteSpace(xmlDocs.Returns) &&
            methodSymbol.GetReturnTypeAttributes().All(attr => 
                !SymbolEqualityComparer.Default.Equals(attr.AttributeClass, descriptionAttribute));

        // Check if any parameters need Description attributes
        var needsParameterDescription = methodSymbol.Parameters.Any(param =>
            !HasAttribute(param, descriptionAttribute) && xmlDocs.Parameters.ContainsKey(param.Name));

        return needsMethodDescription || needsReturnDescription || needsParameterDescription;
    }

    private static XmlDocumentation? ExtractXmlDocumentation(IMethodSymbol methodSymbol)
    {
        var xmlDoc = methodSymbol.GetDocumentationCommentXml();
        if (string.IsNullOrWhiteSpace(xmlDoc))
        {
            return null;
        }

        try
        {
            var doc = XDocument.Parse(xmlDoc);
            var memberElement = doc.Element("member");
            if (memberElement is null)
            {
                return null;
            }

            var summary = CleanXmlDocText(memberElement.Element("summary")?.Value);
            var remarks = CleanXmlDocText(memberElement.Element("remarks")?.Value);
            var returns = CleanXmlDocText(memberElement.Element("returns")?.Value);

            // Combine summary and remarks for method description
            var methodDescription = string.IsNullOrWhiteSpace(remarks) 
                ? summary 
                : string.IsNullOrWhiteSpace(summary) 
                    ? remarks 
                    : $"{summary} {remarks}";

            var paramDocs = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var paramElement in memberElement.Elements("param"))
            {
                var name = paramElement.Attribute("name")?.Value;
                var value = CleanXmlDocText(paramElement.Value);
                if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(value))
                {
                    paramDocs[name!] = value;
                }
            }

            // Only return null if there's no documentation at all
            if (string.IsNullOrWhiteSpace(methodDescription) && string.IsNullOrWhiteSpace(returns) && paramDocs.Count == 0)
            {
                return null;
            }

            return new XmlDocumentation
            {
                MethodDescription = methodDescription,
                Returns = returns,
                Parameters = paramDocs
            };
        }
        catch (System.Xml.XmlException)
        {
            // Invalid XML in documentation comments - skip this method
            return null;
        }
    }

    private static string CleanXmlDocText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        // Remove leading/trailing whitespace and normalize line breaks
        var lines = text!.Split('\n')
            .Select(line => line.Trim())
            .Where(line => !string.IsNullOrEmpty(line));

        return string.Join(" ", lines).Trim();
    }

    private static string GenerateSourceFile(
        List<(IMethodSymbol MethodSymbol, MethodDeclarationSyntax MethodDeclaration, XmlDocumentation XmlDocs)> methods,
        INamedTypeSymbol descriptionAttribute)
    {
        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("#nullable disable");
#if !DEBUG
        sb.AppendLine("#pragma warning disable");
#endif
        sb.AppendLine();
        sb.AppendLine("using System.ComponentModel;");
        sb.AppendLine("using ModelContextProtocol.Server;");
        sb.AppendLine();

        // Group methods by namespace and containing type
        var groupedMethods = methods
            .GroupBy(m => m.MethodSymbol.ContainingNamespace?.ToDisplayString() ?? "<global>")
            .Where(g => g.Key != "<global namespace>");

        foreach (var namespaceGroup in groupedMethods)
        {
            sb.AppendLine($"namespace {namespaceGroup.Key}");
            sb.AppendLine("{");

            // Group by containing type within namespace
            var typeGroups = namespaceGroup.GroupBy(m => m.MethodSymbol.ContainingType, SymbolEqualityComparer.Default);

            foreach (var typeGroup in typeGroups)
            {
                var containingType = typeGroup.Key as INamedTypeSymbol;
                if (containingType is null)
                {
                    continue;
                }

                // Calculate nesting depth for proper indentation
                var nestingDepth = 1;
                var temp = containingType;
                while (temp is not null)
                {
                    nestingDepth++;
                    temp = temp.ContainingType;
                }

                // Handle nested types by building the full type hierarchy
                AppendNestedTypeDeclarations(sb, containingType, 1, () =>
                {
                    // Generate methods for this type
                    foreach (var (methodSymbol, methodDeclaration, xmlDocs) in typeGroup)
                    {
                        AppendMethodDeclaration(sb, methodSymbol, methodDeclaration, xmlDocs, descriptionAttribute, nestingDepth);
                    }
                });

                sb.AppendLine();
            }

            sb.AppendLine("}");
        }

        return sb.ToString();
    }

    private static void AppendNestedTypeDeclarations(StringBuilder sb, INamedTypeSymbol typeSymbol, int indentLevel, System.Action appendBody)
    {
        var types = new Stack<INamedTypeSymbol>();

        // Build stack of nested types from innermost to outermost
        var current = typeSymbol;
        while (current is not null)
        {
            types.Push(current);
            current = current.ContainingType;
        }

        var startIndentLevel = indentLevel;
        var nestingCount = types.Count;

        // Generate type declarations from outermost to innermost
        while (types.Count > 0)
        {
            var type = types.Pop();
            var typeIndent = new string(' ', indentLevel * 4);

            // Get the type keyword and handle records
            var typeDecl = type.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() as TypeDeclarationSyntax;
            string typeKeyword;
            if (typeDecl is RecordDeclarationSyntax rds)
            {
                var classOrStruct = rds.ClassOrStructKeyword.IsKind(SyntaxKind.None) 
                    ? "class" 
                    : rds.ClassOrStructKeyword.ValueText;
                typeKeyword = $"{typeDecl.Keyword.ValueText} {classOrStruct}";
            }
            else
            {
                typeKeyword = type.TypeKind switch
                {
                    TypeKind.Class => "class",
                    TypeKind.Struct => "struct",
                    TypeKind.Interface => "interface",
                    _ => "class"
                };
            }

            // Build modifiers
            var modifiers = new List<string>();
            if (type.DeclaredAccessibility == Accessibility.Public)
            {
                modifiers.Add("public");
            }
            else if (type.DeclaredAccessibility == Accessibility.Internal)
            {
                modifiers.Add("internal");
            }

            if (type.IsStatic)
            {
                modifiers.Add("static");
            }

            modifiers.Add("partial");

            sb.AppendLine($"{typeIndent}{string.Join(" ", modifiers)} {typeKeyword} {type.Name}");
            sb.AppendLine($"{typeIndent}{{");

            indentLevel++;
        }

        // Append the body (methods)
        appendBody();

        // Close all type declarations
        for (int i = 0; i < nestingCount; i++)
        {
            indentLevel--;
            var typeIndent = new string(' ', indentLevel * 4);
            sb.AppendLine($"{typeIndent}}}");
        }
    }

    private static void AppendMethodDeclaration(
        StringBuilder sb,
        IMethodSymbol methodSymbol,
        MethodDeclarationSyntax methodDeclaration,
        XmlDocumentation xmlDocs,
        INamedTypeSymbol descriptionAttribute,
        int indentLevel)
    {
        var indent = new string(' ', indentLevel * 4);

        // Blank line before method

        sb.AppendLine();

        // Check if method needs Description attribute
        var needsMethodDescription = !string.IsNullOrWhiteSpace(xmlDocs.MethodDescription) && 
                                      !HasAttribute(methodSymbol, descriptionAttribute);

        // Add the Description attribute for method if needed
        if (needsMethodDescription)
        {
            sb.AppendLine($"{indent}[Description(\"{EscapeString(xmlDocs.MethodDescription)}\")]");
        }

        // Check if return value needs Description attribute
        var needsReturnDescription = !string.IsNullOrWhiteSpace(xmlDocs.Returns) &&
            methodSymbol.GetReturnTypeAttributes().All(attr => 
                !SymbolEqualityComparer.Default.Equals(attr.AttributeClass, descriptionAttribute));

        // Add return: Description attribute if needed
        if (needsReturnDescription)
        {
            sb.AppendLine($"{indent}[return: Description(\"{EscapeString(xmlDocs.Returns)}\")]");
        }

        // Copy modifiers from original method syntax
        var modifiers = string.Join(" ", methodDeclaration.Modifiers.Select(m => m.Text));
        sb.Append($"{indent}{modifiers} ");

        // Add return type (without nullable annotations)
        var returnType = methodSymbol.ReturnType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
        sb.Append($"{returnType} ");

        // Add method name
        sb.Append($"{methodSymbol.Name}");

        // Add parameters with their Description attributes
        sb.Append("(");
        var paramStrings = new List<string>();
        foreach (var param in methodSymbol.Parameters)
        {
            var paramParts = new List<string>();

            // Check which parameters need Description attributes
            if (!HasAttribute(param, descriptionAttribute) && xmlDocs.Parameters.ContainsKey(param.Name))
            {
                paramParts.Add($"[Description(\"{EscapeString(xmlDocs.Parameters[param.Name])}\")]");
            }

            var paramType = param.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
            paramParts.Add($"{paramType} {param.Name}");
            paramStrings.Add(string.Join(" ", paramParts));
        }
        sb.Append(string.Join(", ", paramStrings));
        sb.AppendLine(");");
    }

    private static string EscapeString(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }

        // Escape special characters for C# string literals
        return text
            .Replace("\\", "\\\\")  // Backslash must be first
            .Replace("\"", "\\\"")  // Quote
            .Replace("\r", "\\r")   // Carriage return
            .Replace("\n", "\\n")   // Newline
            .Replace("\t", "\\t");  // Tab
    }

    /// <summary>
    /// Represents a method that may need Description attributes generated.
    /// Using a struct for better incremental generator caching.
    /// </summary>
    private readonly struct MethodToGenerate
    {
        public MethodToGenerate(MethodDeclarationSyntax methodDeclaration, IMethodSymbol methodSymbol)
        {
            MethodDeclaration = methodDeclaration;
            MethodSymbol = methodSymbol;
        }

        public MethodDeclarationSyntax MethodDeclaration { get; }
        public IMethodSymbol MethodSymbol { get; }
    }

    /// <summary>
    /// Holds extracted XML documentation for a method.
    /// </summary>
    private sealed class XmlDocumentation
    {
        public string MethodDescription { get; set; } = string.Empty;
        public string Returns { get; set; } = string.Empty;
        public Dictionary<string, string> Parameters { get; set; } = new();
    }
}
