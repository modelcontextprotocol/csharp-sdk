using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using System.CodeDom.Compiler;
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
    private const string McpServerToolAttributeName = "ModelContextProtocol.Server.McpServerToolAttribute";
    private const string McpServerPromptAttributeName = "ModelContextProtocol.Server.McpServerPromptAttribute";
    private const string McpServerResourceAttributeName = "ModelContextProtocol.Server.McpServerResourceAttribute";
    private const string DescriptionAttributeName = "System.ComponentModel.DescriptionAttribute";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // Use ForAttributeWithMetadataName for each MCP attribute type
        var toolMethods = CreateProviderForAttribute(context, McpServerToolAttributeName);
        var promptMethods = CreateProviderForAttribute(context, McpServerPromptAttributeName);
        var resourceMethods = CreateProviderForAttribute(context, McpServerResourceAttributeName);

        // Combine all three providers
        var allMethods = toolMethods
            .Collect()
            .Combine(promptMethods.Collect())
            .Combine(resourceMethods.Collect())
            .Select(static (tuple, _) =>
            {
                var ((tool, prompt), resource) = tuple;
                return tool.AddRange(prompt).AddRange(resource);
            });

        // Combine with compilation to get well-known type symbols.
        var compilationAndMethods = context.CompilationProvider.Combine(allMethods);

        // Write out the source for all methods.
        context.RegisterSourceOutput(compilationAndMethods, static (spc, source) => Execute(source.Left, source.Right, spc));
    }

    private static IncrementalValuesProvider<MethodToGenerate> CreateProviderForAttribute(
        IncrementalGeneratorInitializationContext context,
        string attributeMetadataName) =>
        context.SyntaxProvider.ForAttributeWithMetadataName(
            attributeMetadataName,
            static (node, _) => node is MethodDeclarationSyntax,
            static (ctx, ct) =>
            {
                var methodDeclaration = (MethodDeclarationSyntax)ctx.TargetNode;
                var methodSymbol = (IMethodSymbol)ctx.TargetSymbol;
                return new MethodToGenerate(methodDeclaration, methodSymbol);
            });

    private static void Execute(Compilation compilation, ImmutableArray<MethodToGenerate> methods, SourceProductionContext context)
    {
        if (methods.IsDefaultOrEmpty ||
            compilation.GetTypeByMetadataName(DescriptionAttributeName) is not { } descriptionAttribute)
        {
            return;
        }

        // Gather a list of all methods needing generation.
        List<(IMethodSymbol MethodSymbol, MethodDeclarationSyntax MethodDeclaration, XmlDocumentation? XmlDocs)> methodsToGenerate = new(methods.Length);
        foreach (var methodModel in methods)
        {
            var xmlDocs = ExtractXmlDocumentation(methodModel.MethodSymbol, context);

            // Generate implementation for partial methods.
            if (methodModel.MethodDeclaration.Modifiers.Any(SyntaxKind.PartialKeyword))
            {
                methodsToGenerate.Add((methodModel.MethodSymbol, methodModel.MethodDeclaration, xmlDocs));
            }
            else if (xmlDocs is not null && HasGeneratableContent(xmlDocs, methodModel.MethodSymbol, descriptionAttribute))
            {
                // The method is not partial but has XML docs that would generate attributes; issue a diagnostic.
                context.ReportDiagnostic(Diagnostic.Create(
                    Diagnostics.McpMethodMustBePartial,
                    methodModel.MethodDeclaration.Identifier.GetLocation(),
                    methodModel.MethodSymbol.Name));
            }
        }

        // Generate a single file with all partial declarations.
        if (methodsToGenerate.Count > 0)
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
            return new(methodDescription ?? string.Empty, returns ?? string.Empty, paramDocs);
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
        StringWriter sw = new();
        IndentedTextWriter writer = new(sw);

        writer.WriteLine("// <auto-generated/>");
        writer.WriteLine($"// ModelContextProtocol.Analyzers {typeof(XmlToDescriptionGenerator).Assembly.GetName().Version}");
        writer.WriteLine();
        writer.WriteLine("#pragma warning disable");
        writer.WriteLine();
        writer.WriteLine("using System.ComponentModel;");
        writer.WriteLine("using ModelContextProtocol.Server;");
        writer.WriteLine();

        // Group methods by namespace and containing type
        var groupedMethods = methods.GroupBy(m => 
            m.MethodSymbol.ContainingNamespace.Name == compilation.GlobalNamespace.Name ? "" :
            m.MethodSymbol.ContainingNamespace?.ToDisplayString() ??
            "");

        bool firstNamespace = true;
        foreach (var namespaceGroup in groupedMethods)
        {
            if (!firstNamespace)
            {
                writer.WriteLine();
            }
            firstNamespace = false;

            // Check if this is the global namespace (methods with null ContainingNamespace)
            bool isGlobalNamespace = string.IsNullOrEmpty(namespaceGroup.Key);
            if (!isGlobalNamespace)
            {
                writer.WriteLine($"namespace {namespaceGroup.Key}");
                writer.WriteLine("{");
                writer.Indent++;
            }

            // Group by containing type within namespace
            bool isFirstTypeInNamespace = true;
            foreach (var typeGroup in namespaceGroup.GroupBy(m => m.MethodSymbol.ContainingType, SymbolEqualityComparer.Default))
            {
                if (typeGroup.Key is not INamedTypeSymbol containingType)
                {
                    continue;
                }

                if (!isFirstTypeInNamespace)
                {
                    writer.WriteLine();
                }
                isFirstTypeInNamespace = false;

                // Write out the type, which could include parent types.
                AppendNestedTypeDeclarations(writer, containingType, typeGroup, descriptionAttribute);
            }

            if (!isGlobalNamespace)
            {
                writer.Indent--;
                writer.WriteLine("}");
            }
        }

        return sw.ToString();
    }

    private static void AppendNestedTypeDeclarations(
        IndentedTextWriter writer,
        INamedTypeSymbol typeSymbol,
        IGrouping<ISymbol?, (IMethodSymbol MethodSymbol, MethodDeclarationSyntax MethodDeclaration, XmlDocumentation? XmlDocs)> typeGroup,
        INamedTypeSymbol descriptionAttribute)
    {
        // Build stack of nested types from innermost to outermost
        Stack<INamedTypeSymbol> types = [];
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
                string classOrStruct = rds.ClassOrStructKeyword.ValueText;
                if (string.IsNullOrEmpty(classOrStruct))
                {
                    classOrStruct = "class";
                }

                typeKeyword = $"{typeDecl.Keyword.ValueText} {classOrStruct}";
            }
            else
            {
                typeKeyword = typeDecl?.Keyword.ValueText ?? "class";
            }

            writer.WriteLine($"partial {typeKeyword} {type.Name}");
            writer.WriteLine("{");
            writer.Indent++;
        }

        // Generate methods for this type.
        bool firstMethodInType = true;
        foreach (var (methodSymbol, methodDeclaration, xmlDocs) in typeGroup)
        {
            AppendMethodDeclaration(writer, methodSymbol, methodDeclaration, xmlDocs, descriptionAttribute, firstMethodInType);
            firstMethodInType = false;
        }

        // Close all type declarations.
        for (int i = 0; i < nestingCount; i++)
        {
            writer.Indent--;
            writer.WriteLine("}");
        }
    }

    private static void AppendMethodDeclaration(
        IndentedTextWriter writer,
        IMethodSymbol methodSymbol,
        MethodDeclarationSyntax methodDeclaration,
        XmlDocumentation? xmlDocs,
        INamedTypeSymbol descriptionAttribute,
        bool firstMethodInType)
    {
        if (!firstMethodInType)
        {
            writer.WriteLine();
        }

        // Add the Description attribute for method if needed and documentation exists
        if (xmlDocs is not null &&
            !string.IsNullOrWhiteSpace(xmlDocs.MethodDescription) &&
            !HasAttribute(methodSymbol, descriptionAttribute))
        {
            writer.WriteLine($"[Description(\"{EscapeString(xmlDocs.MethodDescription)}\")]");
        }

        // Add return: Description attribute if needed and documentation exists
        if (xmlDocs is not null &&
            !string.IsNullOrWhiteSpace(xmlDocs.Returns) &&
            methodSymbol.GetReturnTypeAttributes().All(attr => !SymbolEqualityComparer.Default.Equals(attr.AttributeClass, descriptionAttribute)))
        {
            writer.WriteLine($"[return: Description(\"{EscapeString(xmlDocs.Returns)}\")]");
        }

        // Copy modifiers from original method syntax.
        // Add return type (without nullable annotations).
        // Add method name.
        writer.Write(string.Join(" ", methodDeclaration.Modifiers.Select(m => m.Text)));
        writer.Write(' ');
        writer.Write(methodSymbol.ReturnType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat));
        writer.Write(' ');
        writer.Write(methodSymbol.Name);

        // Add parameters with their Description attributes.
        writer.Write("(");
        for (int i = 0; i < methodSymbol.Parameters.Length; i++)
        {
            IParameterSymbol param = methodSymbol.Parameters[i];

            if (i > 0)
            {
                writer.Write(", ");
            }

            if (xmlDocs is not null &&
                !HasAttribute(param, descriptionAttribute) && 
                xmlDocs.Parameters.TryGetValue(param.Name, out var paramDoc) &&
                !string.IsNullOrWhiteSpace(paramDoc))
            {
                writer.Write($"[Description(\"{EscapeString(paramDoc)}\")] ");
            }

            writer.Write(param.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat));
            writer.Write(' ');
            writer.Write(param.Name);
        }
        writer.WriteLine(");");
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

    /// <summary>Checks if XML documentation would generate any Description attributes for a method.</summary>
    private static bool HasGeneratableContent(XmlDocumentation xmlDocs, IMethodSymbol methodSymbol, INamedTypeSymbol descriptionAttribute)
    {
        // Check if method description would be generated
        if (!string.IsNullOrWhiteSpace(xmlDocs.MethodDescription) && !HasAttribute(methodSymbol, descriptionAttribute))
        {
            return true;
        }

        // Check if return description would be generated
        if (!string.IsNullOrWhiteSpace(xmlDocs.Returns) &&
            methodSymbol.GetReturnTypeAttributes().All(attr => !SymbolEqualityComparer.Default.Equals(attr.AttributeClass, descriptionAttribute)))
        {
            return true;
        }

        // Check if any parameter descriptions would be generated
        foreach (var param in methodSymbol.Parameters)
        {
            if (!HasAttribute(param, descriptionAttribute) &&
                xmlDocs.Parameters.TryGetValue(param.Name, out var paramDoc) &&
                !string.IsNullOrWhiteSpace(paramDoc))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Represents a method that may need Description attributes generated.</summary>
    private readonly record struct MethodToGenerate(MethodDeclarationSyntax MethodDeclaration, IMethodSymbol MethodSymbol);

    /// <summary>Holds extracted XML documentation for a method.</summary>
    private sealed record XmlDocumentation(string MethodDescription, string Returns, Dictionary<string, string> Parameters);
}
