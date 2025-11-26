using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using System.CodeDom.Compiler;
using System.Collections.Immutable;
using System.Text;
using System.Xml;
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
        // Extract method information for all MCP tools, prompts, and resources.
        var allMethods = CreateProviderForAttribute(context, McpServerToolAttributeName).Collect()
            .Combine(CreateProviderForAttribute(context, McpServerPromptAttributeName).Collect())
            .Combine(CreateProviderForAttribute(context, McpServerResourceAttributeName).Collect())
            .Select(static (tuple, _) =>
            {
                var ((tools, prompts), resources) = tuple;
                return tools.AddRange(prompts).AddRange(resources);
            });

        // Report diagnostics.
        context.RegisterSourceOutput(allMethods, static (spc, methods) =>
        {
            foreach (var method in methods)
            {
                foreach (var diagnostic in method.Diagnostics)
                {
                    spc.ReportDiagnostic(CreateDiagnostic(diagnostic));
                }
            }
        });

        // Generate source code.
        context.RegisterSourceOutput(
            allMethods.SelectMany(static (methods, _) => methods.Where(static m => m.NeedsGeneration)).Collect(),
            static (spc, methods) =>
            {
                if (!methods.IsDefaultOrEmpty)
                {
                    spc.AddSource(GeneratedFileName, SourceText.From(GenerateSourceFile(methods), Encoding.UTF8));
                }
            });
    }

    private static IncrementalValuesProvider<MethodToGenerate> CreateProviderForAttribute(
        IncrementalGeneratorInitializationContext context,
        string attributeMetadataName) =>
        context.SyntaxProvider.ForAttributeWithMetadataName(
            attributeMetadataName,
            static (node, _) => node is MethodDeclarationSyntax,
            static (ctx, ct) => ExtractMethodInfo((MethodDeclarationSyntax)ctx.TargetNode, (IMethodSymbol)ctx.TargetSymbol, ctx.SemanticModel.Compilation));

    private static MethodToGenerate ExtractMethodInfo(
        MethodDeclarationSyntax methodDeclaration,
        IMethodSymbol methodSymbol,
        Compilation compilation)
    {
        bool isPartial = methodDeclaration.Modifiers.Any(SyntaxKind.PartialKeyword);
        var xmlDocs = ExtractXmlDocumentation(methodSymbol);

        // If the method is not partial and has no XML docs, bail (but check to see if the lack of XML docs
        // is because they're malformed).
        if (!isPartial && xmlDocs is null)
        {
            return CreateXmlDiagnosticInfoIfInvalid(methodSymbol) is { } xmlDiagnostic ?
                MethodToGenerate.Create(ImmutableArray.Create(xmlDiagnostic)) :
                MethodToGenerate.Create(ImmutableArray<DiagnosticInfo>.Empty);
        }

        var descriptionAttribute = compilation.GetTypeByMetadataName(DescriptionAttributeName);

        bool needsMethodDescription = xmlDocs is not null &&
            !string.IsNullOrWhiteSpace(xmlDocs.MethodDescription) &&
            (descriptionAttribute is null || !HasAttribute(methodSymbol, descriptionAttribute));

        bool needsReturnDescription = xmlDocs is not null &&
            !string.IsNullOrWhiteSpace(xmlDocs.Returns) &&
            (descriptionAttribute is null ||
             methodSymbol.GetReturnTypeAttributes().All(attr => !SymbolEqualityComparer.Default.Equals(attr.AttributeClass, descriptionAttribute)));

        bool needsParameterDescription = xmlDocs is not null &&
            xmlDocs.Parameters.Count > 0 &&
            (descriptionAttribute is null ||
             methodSymbol.Parameters.Any(p => xmlDocs.Parameters.ContainsKey(p.Name) && !HasAttribute(p, descriptionAttribute)));

        // If not partial, check if we'd issue diagnostics before doing expensive extraction work.
        if (!isPartial && !needsMethodDescription && !needsReturnDescription && !needsParameterDescription)
        {
            return CreateXmlDiagnosticInfoIfInvalid(methodSymbol) is { } xmlDiagnostic ?
                MethodToGenerate.Create(ImmutableArray.Create(xmlDiagnostic)) :
                MethodToGenerate.Create(ImmutableArray<DiagnosticInfo>.Empty);
        }

        // There's now real work to be done, extracting the full method info, as we need to do some kind
        // of generation for this method.

        // Extract method info.
        string modifiers = string.Join(" ", methodDeclaration.Modifiers.Select(m => m.Text));
        string returnType = methodSymbol.ReturnType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
        string methodName = methodSymbol.Name;
        var typeParameters = methodSymbol.TypeParameters.Select(tp => tp.Name).ToImmutableArray();
        var parameters = methodSymbol.Parameters.Select(param => new ParameterInfo(
            param.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
            param.Name,
            descriptionAttribute is not null && HasAttribute(param, descriptionAttribute),
            xmlDocs?.Parameters.TryGetValue(param.Name, out var pd) == true ? pd : null)).ToImmutableArray();

        // Gather diagnostics.
        ImmutableArray<DiagnosticInfo> diagnostics = ImmutableArray<DiagnosticInfo>.Empty;
        if (xmlDocs is null)
        {
            if (CreateXmlDiagnosticInfoIfInvalid(methodSymbol) is DiagnosticInfo xmlDiagnostic)
            {
                diagnostics = ImmutableArray.Create(xmlDiagnostic);
            }
        }
        else if (!isPartial && descriptionAttribute is not null &&
                (needsMethodDescription || needsReturnDescription || needsParameterDescription))
        {
            diagnostics = ImmutableArray.Create(new DiagnosticInfo(
                Diagnostics.McpMethodMustBePartial.Id,
                methodDeclaration.Identifier.GetLocation(),
                methodSymbol.Name));
        }

        return new MethodToGenerate(
            NeedsGeneration: isPartial,
            TypeInfo: ExtractTypeInfo(methodSymbol.ContainingType),
            Modifiers: modifiers,
            ReturnType: returnType,
            MethodName: methodName,
            TypeParameters: typeParameters,
            Parameters: parameters,
            MethodDescription: needsMethodDescription ? xmlDocs?.MethodDescription : null,
            ReturnDescription: needsReturnDescription ? xmlDocs?.Returns : null,
            Diagnostics: diagnostics);
    }

    private static DiagnosticInfo? CreateXmlDiagnosticInfoIfInvalid(IMethodSymbol methodSymbol)
    {
        if (methodSymbol.GetDocumentationCommentXml() is { } xmlDoc && !string.IsNullOrWhiteSpace(xmlDoc))
        {
            try
            {
                XDocument.Parse(xmlDoc);
            }
            catch (XmlException)
            {
                return new DiagnosticInfo(
                    Diagnostics.InvalidXmlDocumentation.Id,
                    methodSymbol.Locations.FirstOrDefault(),
                    methodSymbol.Name);
            }
        }

        return null;
    }

    private static TypeInfo ExtractTypeInfo(INamedTypeSymbol? typeSymbol)
    {
        if (typeSymbol is null)
        {
            return new TypeInfo(string.Empty, ImmutableArray<TypeDeclarationInfo>.Empty);
        }

        // Build stack of nested types from innermost to outermost
        var types = ImmutableArray.CreateBuilder<TypeDeclarationInfo>();
        for (var current = typeSymbol; current is not null; current = current.ContainingType)
        {
            var typeDecl = current.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() as TypeDeclarationSyntax;
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

            types.Add(new TypeDeclarationInfo(current.Name, typeKeyword));
        }

        types.Reverse();
        return new TypeInfo(
            typeSymbol.ContainingNamespace.IsGlobalNamespace ? "" : typeSymbol.ContainingNamespace.ToDisplayString(),
            types.ToImmutable());
    }

    private static Diagnostic CreateDiagnostic(DiagnosticInfo info) =>
        Diagnostic.Create(info.Id switch
        {
            "MCP001" => Diagnostics.InvalidXmlDocumentation,
            "MCP002" => Diagnostics.McpMethodMustBePartial,
            _ => throw new InvalidOperationException($"Unknown diagnostic ID: {info.Id}")
        }, info.Location, info.MessageArgs);

    private static XmlDocumentation? ExtractXmlDocumentation(IMethodSymbol methodSymbol)
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
        catch (XmlException)
        {
            // Return null for invalid XML - diagnostic will be reported separately
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

    private static string GenerateSourceFile(ImmutableArray<MethodToGenerate> methods)
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
        var groupedMethods = methods.GroupBy(m => m.TypeInfo.Namespace);

        bool firstNamespace = true;
        foreach (var namespaceGroup in groupedMethods)
        {
            if (!firstNamespace)
            {
                writer.WriteLine();
            }
            firstNamespace = false;

            // Check if this is the global namespace
            bool isGlobalNamespace = string.IsNullOrEmpty(namespaceGroup.Key);
            if (!isGlobalNamespace)
            {
                writer.WriteLine($"namespace {namespaceGroup.Key}");
                writer.WriteLine("{");
                writer.Indent++;
            }

            // Group by containing type within namespace (using structural equality for TypeInfo)
            bool isFirstTypeInNamespace = true;
            foreach (var typeGroup in namespaceGroup.GroupBy(m => m.TypeInfo))
            {
                if (!isFirstTypeInNamespace)
                {
                    writer.WriteLine();
                }
                isFirstTypeInNamespace = false;

                // Write out the type, which could include parent types.
                AppendNestedTypeDeclarations(writer, typeGroup.Key, typeGroup);
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
        TypeInfo typeInfo,
        IEnumerable<MethodToGenerate> methods)
    {
        // Generate type declarations from outermost to innermost
        int nestingCount = typeInfo.Types.Length;
        foreach (var type in typeInfo.Types)
        {
            writer.WriteLine($"partial {type.TypeKeyword} {type.Name}");
            writer.WriteLine("{");
            writer.Indent++;
        }

        // Generate methods for this type.
        bool firstMethodInType = true;
        foreach (var method in methods)
        {
            AppendMethodDeclaration(writer, method, firstMethodInType);
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
        MethodToGenerate method,
        bool firstMethodInType)
    {
        if (!firstMethodInType)
        {
            writer.WriteLine();
        }

        // Add the Description attribute for method if needed
        if (!string.IsNullOrWhiteSpace(method.MethodDescription))
        {
            writer.WriteLine($"[Description(\"{EscapeString(method.MethodDescription!)}\")]");
        }

        // Add return: Description attribute if needed
        if (!string.IsNullOrWhiteSpace(method.ReturnDescription))
        {
            writer.WriteLine($"[return: Description(\"{EscapeString(method.ReturnDescription!)}\")]");
        }

        // Write method signature
        writer.Write(method.Modifiers);
        writer.Write(' ');
        writer.Write(method.ReturnType);
        writer.Write(' ');
        writer.Write(method.MethodName);

        // Add generic type parameters if present
        if (method.TypeParameters.Length > 0)
        {
            writer.Write('<');
            for (int i = 0; i < method.TypeParameters.Length; i++)
            {
                if (i > 0)
                {
                    writer.Write(", ");
                }
                writer.Write(method.TypeParameters[i]);
            }
            writer.Write('>');
        }

        // Add parameters with their Description attributes.
        writer.Write("(");
        for (int i = 0; i < method.Parameters.Length; i++)
        {
            var param = method.Parameters[i];

            if (i > 0)
            {
                writer.Write(", ");
            }

            if (!param.HasDescriptionAttribute && !string.IsNullOrWhiteSpace(param.XmlDescription))
            {
                writer.Write($"[Description(\"{EscapeString(param.XmlDescription!)}\")] ");
            }

            writer.Write(param.ParameterType);
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

    // Cache-friendly data structures - these hold only primitive data, no symbols or syntax

    private readonly record struct MethodToGenerate(
        bool NeedsGeneration, TypeInfo TypeInfo, string Modifiers, string ReturnType, string MethodName,
        ImmutableArray<string> TypeParameters, ImmutableArray<ParameterInfo> Parameters, 
        string? MethodDescription, string? ReturnDescription, ImmutableArray<DiagnosticInfo> Diagnostics)
    {
        public static MethodToGenerate Create(ImmutableArray<DiagnosticInfo> diagnostics) =>
            new(NeedsGeneration: false, TypeInfo: default,
                Modifiers: string.Empty, ReturnType: string.Empty, MethodName: string.Empty,
                TypeParameters: ImmutableArray<string>.Empty, Parameters: ImmutableArray<ParameterInfo>.Empty,
                MethodDescription: null, ReturnDescription: null,
                Diagnostics: diagnostics);
    }

    private readonly record struct ParameterInfo(string ParameterType, string Name, bool HasDescriptionAttribute, string? XmlDescription);

    private readonly record struct TypeInfo(string Namespace, ImmutableArray<TypeDeclarationInfo> Types)
    {
        public bool Equals(TypeInfo other) =>
            Namespace == other.Namespace &&
            Types.SequenceEqual(other.Types);

        public override int GetHashCode()
        {
            int hash = Namespace.GetHashCode();
            foreach (var type in Types)
            {
                hash = hash * 397 ^ type.Name.GetHashCode();
                hash = hash * 397 ^ type.TypeKeyword.GetHashCode();
            }
            return hash;
        }
    }

    private readonly record struct TypeDeclarationInfo(string Name, string TypeKeyword);

    private readonly record struct DiagnosticInfo(string Id, Location? Location, params object?[] MessageArgs);

    private sealed record XmlDocumentation(string MethodDescription, string Returns, Dictionary<string, string> Parameters);
}
