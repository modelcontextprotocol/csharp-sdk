using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Immutable;
using System.Text;
using System.Xml.Linq;

namespace ModelContextProtocol.Analyzers;

/// <summary>
/// Source generator that creates [Description] attributes from XML comments
/// for partial methods tagged with MCP attributes.
/// </summary>
[Generator]
public sealed class XmlToDescriptionGenerator : IIncrementalGenerator
{
    private const string GeneratedFileName = "ModelContextProtocol.Descriptions.g.cs";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // Filter method declarations with attributes. We're looking for attributed partial methods.
        var methodModels = context.SyntaxProvider
            .CreateSyntaxProvider<MethodToGenerate?>(
                static (s, _) => s is MethodDeclarationSyntax { AttributeLists.Count: > 0 } method && method.Modifiers.Any(SyntaxKind.PartialKeyword),
                static (ctx, ct) =>
                {
                    var methodDeclaration = (MethodDeclarationSyntax)ctx.Node;
                    return ctx.SemanticModel.GetDeclaredSymbol(methodDeclaration, ct) is { } methodSymbol ?
                        new MethodToGenerate(methodDeclaration, methodSymbol) :
                        null;
                })
            .Where(static m => m is not null);

        // Combine with compilation to get well-known type symbols.
        var compilationAndMethods = context.CompilationProvider.Combine(methodModels.Collect());

        // Write out the source for all methods.
        context.RegisterSourceOutput(compilationAndMethods, static (spc, source) => Execute(source.Left, source.Right!, spc));
    }

    private static void Execute(Compilation compilation, ImmutableArray<MethodToGenerate?> methods, SourceProductionContext context)
    {
        if (methods.IsDefaultOrEmpty)
        {
            return;
        }

        // Get well-known type symbols upfront. If any of them are missing, give up.
        var toolAttribute = compilation.GetTypeByMetadataName("ModelContextProtocol.Server.McpServerToolAttribute");
        var promptAttribute = compilation.GetTypeByMetadataName("ModelContextProtocol.Server.McpServerPromptAttribute");
        var resourceAttribute = compilation.GetTypeByMetadataName("ModelContextProtocol.Server.McpServerResourceAttribute");
        var descriptionAttribute = compilation.GetTypeByMetadataName("System.ComponentModel.DescriptionAttribute");
        if (descriptionAttribute is null || toolAttribute is null || promptAttribute is null || resourceAttribute is null)
        {
            return;
        }

        // Gather a list of all methods needing generation.
        List<(IMethodSymbol MethodSymbol, MethodDeclarationSyntax MethodDeclaration, XmlDocumentation? XmlDocs)>? methodsToGenerate = null;
        foreach (var methodModel in methods)
        {
            if (methodModel is not null)
            {
                // Check if method has any MCP attribute with symbol comparison
                var methodSymbol = methodModel.Value.MethodSymbol;
                bool hasMcpAttribute =
                    HasAttribute(methodSymbol, toolAttribute) ||
                    HasAttribute(methodSymbol, promptAttribute) ||
                    HasAttribute(methodSymbol, resourceAttribute);
                if (hasMcpAttribute)
                {
                    // Extract XML documentation. Even if there's no documentation or it's invalid,
                    // we still need to generate the partial implementation to avoid compilation errors.
                    var xmlDocs = ExtractXmlDocumentation(methodSymbol, context);
                    
                    // Always add the method to generate its implementation, but emit diagnostics
                    // to guide the developer if documentation is missing or invalid.
                    (methodsToGenerate ??= []).Add((methodSymbol, methodModel.Value.MethodDeclaration, xmlDocs));
                }
            }
        }

        // Generate a single file with all partial declarations.
        if (methodsToGenerate is not null)
        {
            string source = GenerateSourceFile(compilation, methodsToGenerate, descriptionAttribute);
            context.AddSource(GeneratedFileName, SourceText.From(source, Encoding.UTF8));
        }
    }

    private static XmlDocumentation? ExtractXmlDocumentation(IMethodSymbol methodSymbol, SourceProductionContext context)
    {
        string? xmlDoc = methodSymbol.GetDocumentationCommentXml();
        if (string.IsNullOrWhiteSpace(xmlDoc))
        {
            return null;
        }

        try
        {
            if (XDocument.Parse(xmlDoc).Element("member") is not { } memberElement)
            {
                return null;
            }

            var summary = CleanXmlDocText(memberElement.Element("summary")?.Value);
            var remarks = CleanXmlDocText(memberElement.Element("remarks")?.Value);
            var returns = CleanXmlDocText(memberElement.Element("returns")?.Value);

            // Combine summary and remarks for method description.
            var methodDescription = 
                string.IsNullOrWhiteSpace(remarks) ? summary :
                string.IsNullOrWhiteSpace(summary) ? remarks :
                $"{summary}\n{remarks}";

            Dictionary<string, string> paramDocs = new(StringComparer.Ordinal);
            foreach (var paramElement in memberElement.Elements("param"))
            {
                var name = paramElement.Attribute("name")?.Value;
                var value = CleanXmlDocText(paramElement.Value);
                if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(value))
                {
                    paramDocs[name!] = value;
                }
            }

            // Return documentation even if empty - we'll still generate the partial implementation
            return new XmlDocumentation(methodDescription ?? string.Empty, returns ?? string.Empty, paramDocs);
        }
        catch (System.Xml.XmlException)
        {
            // Emit warning for invalid XML
            context.ReportDiagnostic(Diagnostic.Create(
                Diagnostics.InvalidXmlDocumentation,
                methodSymbol.Locations.FirstOrDefault(),
                methodSymbol.Name));
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
        Compilation compilation,
        List<(IMethodSymbol MethodSymbol, MethodDeclarationSyntax MethodDeclaration, XmlDocumentation? XmlDocs)> methods,
        INamedTypeSymbol descriptionAttribute)
    {
        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated/>")
          .AppendLine($"// ModelContextProtocol.Analyzers {typeof(XmlToDescriptionGenerator).Assembly.GetName().Version}")
          .AppendLine()
          .AppendLine("#pragma warning disable")
          .AppendLine()
          .AppendLine("using System.ComponentModel;")
          .AppendLine("using ModelContextProtocol.Server;")
          .AppendLine();

        // Group methods by namespace and containing type
        var groupedMethods = methods.GroupBy(m => 
            m.MethodSymbol.ContainingNamespace.Name == compilation.GlobalNamespace.Name ? "" :
            m.MethodSymbol.ContainingNamespace?.ToDisplayString() ??
            "");

        foreach (var namespaceGroup in groupedMethods)
        {
            // Check if this is the global namespace (methods with null ContainingNamespace)
            bool isGlobalNamespace = string.IsNullOrEmpty(namespaceGroup.Key);
            if (!isGlobalNamespace)
            {
                sb.Append("namespace ")
                  .AppendLine(namespaceGroup.Key)
                  .AppendLine("{");
            }

            // Group by containing type within namespace
            var typeGroups = namespaceGroup.GroupBy(m => m.MethodSymbol.ContainingType, SymbolEqualityComparer.Default);

            foreach (var typeGroup in typeGroups)
            {
                if (typeGroup.Key is not INamedTypeSymbol containingType)
                {
                    continue;
                }

                // Calculate nesting depth for proper indentation
                // For global namespace, start at 0; for namespaced types, start at 1
                int nestingDepth = isGlobalNamespace ? 0 : 1;
                var temp = containingType;
                while (temp is not null)
                {
                    nestingDepth++;
                    temp = temp.ContainingType;
                }

                // Handle nested types by building the full type hierarchy
                int startIndent = isGlobalNamespace ? 0 : 1;
                AppendNestedTypeDeclarations(sb, containingType, startIndent, typeGroup, descriptionAttribute, nestingDepth);

                sb.AppendLine();
            }

            if (!isGlobalNamespace)
            {
                sb.AppendLine("}");
            }
        }

        return sb.ToString();
    }

    private static void AppendNestedTypeDeclarations(
        StringBuilder sb, 
        INamedTypeSymbol typeSymbol, 
        int indentLevel,
        IGrouping<ISymbol?, (IMethodSymbol MethodSymbol, MethodDeclarationSyntax MethodDeclaration, XmlDocumentation? XmlDocs)> typeGroup,
        INamedTypeSymbol descriptionAttribute,
        int nestingDepth)
    {
        // Build stack of nested types from innermost to outermost
        Stack<INamedTypeSymbol> types = new();
        for (var current = typeSymbol; current is not null; current = current.ContainingType)
        {
            types.Push(current);
        }

        // Generate type declarations from outermost to innermost
        int nestingCount = types.Count;
        while (types.Count > 0)
        {
            // Get the type keyword and handle records
            var type = types.Pop();
            var typeDecl = type.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() as TypeDeclarationSyntax;
            string typeKeyword;
            if (typeDecl is RecordDeclarationSyntax rds)
            {
                var classOrStruct = rds.ClassOrStructKeyword.ValueText;
                typeKeyword = string.IsNullOrEmpty(classOrStruct)  ?
                    $"{typeDecl.Keyword.ValueText} class" :
                    $"{typeDecl.Keyword.ValueText} {classOrStruct}";
            }
            else
            {
                typeKeyword = typeDecl?.Keyword.ValueText ?? "class";
            }

            sb.Append(' ', indentLevel * 4).Append("partial ").Append(typeKeyword).Append(' ').AppendLine(type.Name)
              .Append(' ', indentLevel * 4).AppendLine("{");

            indentLevel++;
        }

        // Generate methods for this type.
        bool firstMethodInType = true;
        foreach (var (methodSymbol, methodDeclaration, xmlDocs) in typeGroup)
        {
            AppendMethodDeclaration(sb, methodSymbol, methodDeclaration, xmlDocs, descriptionAttribute, firstMethodInType, nestingDepth);
            firstMethodInType = false;
        }

        // Close all type declarations.
        for (int i = 0; i < nestingCount; i++)
        {
            indentLevel--;
            sb.Append(' ', indentLevel * 4).AppendLine("}");
        }
    }

    private static void AppendMethodDeclaration(
        StringBuilder sb,
        IMethodSymbol methodSymbol,
        MethodDeclarationSyntax methodDeclaration,
        XmlDocumentation? xmlDocs,
        INamedTypeSymbol descriptionAttribute,
        bool firstMethodInType,
        int indentLevel)
    {
        int indent = indentLevel * 4;

        if (!firstMethodInType)
        {
            sb.AppendLine();
        }

        // Add the Description attribute for method if needed and documentation exists
        if (xmlDocs is not null &&
            !string.IsNullOrWhiteSpace(xmlDocs.MethodDescription) &&
            !HasAttribute(methodSymbol, descriptionAttribute))
        {
            sb.Append(' ', indent)
              .Append("[Description(\"")
              .Append(EscapeString(xmlDocs.MethodDescription))
              .AppendLine("\")]");
        }

        // Add return: Description attribute if needed and documentation exists
        if (xmlDocs is not null &&
            !string.IsNullOrWhiteSpace(xmlDocs.Returns) &&
            methodSymbol.GetReturnTypeAttributes().All(attr => !SymbolEqualityComparer.Default.Equals(attr.AttributeClass, descriptionAttribute)))
        {
            sb.Append(' ', indent)
              .Append("[return: Description(\"")
              .Append(EscapeString(xmlDocs.Returns))
              .AppendLine("\")]");
        }

        // Copy modifiers from original method syntax.
        // Add return type (without nullable annotations).
        // Add method name.
        sb.Append(' ', indent)
          .Append(string.Join(" ", methodDeclaration.Modifiers.Select(m => m.Text)))
          .Append(' ')
          .Append(methodSymbol.ReturnType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat))
          .Append(' ')
          .Append(methodSymbol.Name);

        // Add parameters with their Description attributes.
        sb.Append("(");
        for (int i = 0; i < methodSymbol.Parameters.Length; i++)
        {
            IParameterSymbol param = methodSymbol.Parameters[i];

            if (i > 0)
            {
                sb.Append(", ");
            }

            if (xmlDocs is not null &&
                !HasAttribute(param, descriptionAttribute) && 
                xmlDocs.Parameters.TryGetValue(param.Name, out var paramDoc) &&
                !string.IsNullOrWhiteSpace(paramDoc))
            {
                sb.Append("[Description(\"")
                  .Append(EscapeString(paramDoc))
                  .Append("\")] ");
            }

            sb.Append(param.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat))
              .Append(' ')
              .Append(param.Name);
        }
        sb.AppendLine(");");
    }

    /// <summary>Checks if a symbol has a specific attribute applied.</summary>
    private static bool HasAttribute(ISymbol symbol, INamedTypeSymbol attributeType)
    {
        foreach (var attr in symbol.GetAttributes())
        {
            if (SymbolEqualityComparer.Default.Equals(attr.AttributeClass, attributeType))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Escape special characters for C# string literals.</summary>
    private static string EscapeString(string text) =>
        string.IsNullOrEmpty(text) ? text :
        text.Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\r", "\\r")
            .Replace("\n", "\\n")
            .Replace("\t", "\\t");

    /// <summary>Represents a method that may need Description attributes generated.</summary>
    private readonly record struct MethodToGenerate(MethodDeclarationSyntax MethodDeclaration, IMethodSymbol MethodSymbol);

    /// <summary>Holds extracted XML documentation for a method.</summary>
    private sealed record XmlDocumentation(string MethodDescription, string Returns, Dictionary<string, string> Parameters);
}
