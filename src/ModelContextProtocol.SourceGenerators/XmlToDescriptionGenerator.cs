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
/// for partial methods tagged with McpServerTool attribute.
/// </summary>
[Generator]
public class XmlToDescriptionGenerator : IIncrementalGenerator
{
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
        
        cancellationToken.ThrowIfCancellationRequested();
        
        var methodSymbol = context.SemanticModel.GetDeclaredSymbol(methodDeclaration, cancellationToken);
        if (methodSymbol == null)
        {
            return null;
        }

        // Check for McpServerTool attribute early (before expensive XML parsing)
        var hasMcpAttribute = methodSymbol.GetAttributes().Any(attr => 
            attr.AttributeClass?.Name is "McpServerToolAttribute" or "McpServerTool");
        
        if (!hasMcpAttribute)
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
        var descriptionAttribute = compilation.GetTypeByMetadataName("System.ComponentModel.DescriptionAttribute");

        if (descriptionAttribute == null)
        {
            // Description attribute is required - can't generate without it
            return;
        }

        foreach (var methodModel in methods)
        {
            if (methodModel == null)
            {
                continue;
            }

            context.CancellationToken.ThrowIfCancellationRequested();

            var methodSymbol = methodModel.Value.MethodSymbol;
            var methodDeclaration = methodModel.Value.MethodDeclaration;

            // Double-check McpServerTool attribute with symbol comparison if available
            if (mcpServerToolAttribute != null && !HasAttribute(methodSymbol, mcpServerToolAttribute))
            {
                continue;
            }

            // Extract XML documentation
            var xmlDocs = ExtractXmlDocumentation(methodSymbol);
            if (xmlDocs == null)
            {
                continue;
            }

            // Generate the partial method declaration with Description attributes
            var source = GeneratePartialMethodDeclaration(methodSymbol, methodDeclaration, xmlDocs, descriptionAttribute);
            if (source != null)
            {
                var hintName = GetHintName(methodSymbol);
                context.AddSource(hintName, SourceText.From(source, Encoding.UTF8));
            }
        }
    }

    private static bool HasAttribute(ISymbol symbol, INamedTypeSymbol attributeType)
    {
        return symbol.GetAttributes().Any(attr => 
            SymbolEqualityComparer.Default.Equals(attr.AttributeClass, attributeType));
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
            if (memberElement == null)
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

            if (string.IsNullOrWhiteSpace(methodDescription))
            {
                return null;
            }

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

    private static string? GeneratePartialMethodDeclaration(
        IMethodSymbol methodSymbol, 
        MethodDeclarationSyntax methodDeclaration, 
        XmlDocumentation xmlDocs,
        INamedTypeSymbol descriptionAttribute)
    {
        var containingType = methodSymbol.ContainingType;
        if (containingType == null)
        {
            return null;
        }

        var namespaceName = containingType.ContainingNamespace?.ToDisplayString();
        if (string.IsNullOrEmpty(namespaceName) || namespaceName == "<global namespace>")
        {
            return null;
        }

        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("#nullable enable");
        sb.AppendLine();
        sb.AppendLine("using System.ComponentModel;");
        sb.AppendLine("using ModelContextProtocol.Server;");
        sb.AppendLine();
        sb.AppendLine($"namespace {namespaceName};");
        sb.AppendLine();

        // Check if method needs Description attribute
        var needsMethodDescription = !HasAttribute(methodSymbol, descriptionAttribute);
        
        // Check which parameters need Description attributes
        var parametersNeedingDescription = new HashSet<string>();
        foreach (var param in methodSymbol.Parameters)
        {
            if (!HasAttribute(param, descriptionAttribute) && xmlDocs.Parameters.ContainsKey(param.Name))
            {
                parametersNeedingDescription.Add(param.Name);
            }
        }

        // Check if return value needs Description attribute
        var needsReturnDescription = !string.IsNullOrWhiteSpace(xmlDocs.Returns) &&
            methodSymbol.GetReturnTypeAttributes().All(attr => 
                !SymbolEqualityComparer.Default.Equals(attr.AttributeClass, descriptionAttribute));

        // Only generate if there's something to add
        if (!needsMethodDescription && parametersNeedingDescription.Count == 0 && !needsReturnDescription)
        {
            return null;
        }

        // Generate the containing type
        AppendTypeDeclaration(sb, containingType, () =>
        {
            // Add the Description attribute for method if needed
            if (needsMethodDescription)
            {
                sb.AppendLine($"    [Description(\"{EscapeString(xmlDocs.MethodDescription)}\")]");
            }

            // Add return: Description attribute if needed
            if (needsReturnDescription)
            {
                sb.AppendLine($"    [return: Description(\"{EscapeString(xmlDocs.Returns)}\")]");
            }
            
            // Copy all modifiers
            var modifiers = string.Join(" ", methodDeclaration.Modifiers.Select(m => m.Text));
            sb.Append($"    {modifiers} ");

            // Add return type
            sb.Append($"{methodSymbol.ReturnType.ToDisplayString()} ");

            // Add method name
            sb.Append($"{methodSymbol.Name}");

            // Add parameters with their Description attributes
            sb.Append("(");
            var paramStrings = new List<string>();
            foreach (var param in methodSymbol.Parameters)
            {
                var paramParts = new List<string>();
                
                // Add Description attribute if needed
                if (parametersNeedingDescription.Contains(param.Name))
                {
                    paramParts.Add($"[Description(\"{EscapeString(xmlDocs.Parameters[param.Name])}\")]");
                }
                
                paramParts.Add($"{param.Type.ToDisplayString()} {param.Name}");
                paramStrings.Add(string.Join(" ", paramParts));
            }
            sb.Append(string.Join(", ", paramStrings));
            sb.AppendLine(");");
        });

        return sb.ToString();
    }

    private static void AppendTypeDeclaration(StringBuilder sb, INamedTypeSymbol typeSymbol, System.Action appendBody)
    {
        var typeKeyword = typeSymbol.TypeKind switch
        {
            TypeKind.Class => "class",
            TypeKind.Struct => "struct",
            TypeKind.Interface => "interface",
            _ => "class"
        };

        var modifiers = new List<string>();
        if (typeSymbol.DeclaredAccessibility == Accessibility.Public)
        {
            modifiers.Add("public");
        }
        else if (typeSymbol.DeclaredAccessibility == Accessibility.Internal)
        {
            modifiers.Add("internal");
        }

        if (typeSymbol.IsStatic)
        {
            modifiers.Add("static");
        }

        modifiers.Add("partial");

        sb.AppendLine($"{string.Join(" ", modifiers)} {typeKeyword} {typeSymbol.Name}");
        sb.AppendLine("{");
        appendBody();
        sb.AppendLine("}");
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

    private static string GetHintName(IMethodSymbol methodSymbol)
    {
        var containingType = methodSymbol.ContainingType;
        var typeName = containingType.Name;
        
        // Include generic arity if present to avoid collisions
        if (containingType.IsGenericType)
        {
            typeName = $"{typeName}`{containingType.Arity}";
        }

        return $"{typeName}.{methodSymbol.Name}.g.cs";
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
