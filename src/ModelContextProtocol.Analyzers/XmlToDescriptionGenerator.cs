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

    /// <summary>
    /// A display format that produces fully-qualified type names with "global::" prefix
    /// and includes nullability annotations.
    /// </summary>
    private static readonly SymbolDisplayFormat s_fullyQualifiedFormatWithNullability =
        SymbolDisplayFormat.FullyQualifiedFormat.AddMiscellaneousOptions(
            SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // Extract method information for all MCP tools, prompts, and resources.
        // The transform extracts all necessary data upfront so the output doesn't depend on the compilation.
        var allMethods = CreateProviderForAttribute(context, McpAttributeNames.McpServerToolAttribute).Collect()
            .Combine(CreateProviderForAttribute(context, McpAttributeNames.McpServerPromptAttribute).Collect())
            .Combine(CreateProviderForAttribute(context, McpAttributeNames.McpServerResourceAttribute).Collect())
            .Select(static (tuple, _) =>
            {
                var ((tools, prompts), resources) = tuple;
                return new EquatableArray<MethodToGenerate>(tools.Concat(prompts).Concat(resources));
            });

        // Report diagnostics for all methods.
        context.RegisterSourceOutput(
            allMethods, 
            static (spc, methods) =>
            {
                foreach (var method in methods)
                {
                    foreach (var diagnostic in method.Diagnostics)
                    {
                        spc.ReportDiagnostic(CreateDiagnostic(diagnostic));
                    }
                }
            });

        // Generate source code only for methods that need generation.
        context.RegisterSourceOutput(
            allMethods.Select(static (methods, _) => new EquatableArray<MethodToGenerate>(methods.Where(m => m.NeedsGeneration))),
            static (spc, methods) =>
            {
                if (methods.Length > 0)
                {
                    spc.AddSource(GeneratedFileName, SourceText.From(GenerateSourceFile(methods), Encoding.UTF8));
                }
            });
    }

    private static Diagnostic CreateDiagnostic(DiagnosticInfo info) =>
        Diagnostic.Create(info.Id switch
        {
            "MCP001" => Diagnostics.InvalidXmlDocumentation,
            "MCP002" => Diagnostics.McpMethodMustBePartial,
            _ => throw new InvalidOperationException($"Unknown diagnostic ID: {info.Id}")
        }, info.Location?.ToLocation(), info.MessageArgs);

    private static IncrementalValuesProvider<MethodToGenerate> CreateProviderForAttribute(
        IncrementalGeneratorInitializationContext context,
        string attributeMetadataName) =>
        context.SyntaxProvider.ForAttributeWithMetadataName(
            attributeMetadataName,
            static (node, _) => node is MethodDeclarationSyntax,
            static (ctx, _) => ExtractMethodInfo((MethodDeclarationSyntax)ctx.TargetNode, (IMethodSymbol)ctx.TargetSymbol, ctx.SemanticModel.Compilation));

    private static MethodToGenerate ExtractMethodInfo(
        MethodDeclarationSyntax methodDeclaration,
        IMethodSymbol methodSymbol,
        Compilation compilation)
    {
        bool isPartial = methodDeclaration.Modifiers.Any(SyntaxKind.PartialKeyword);
        var descriptionAttribute = compilation.GetTypeByMetadataName(McpAttributeNames.DescriptionAttribute);

        // Try to extract XML documentation
        var (xmlDocs, hasInvalidXml) = TryExtractXmlDocumentation(methodSymbol);
        
        // For non-partial methods, check if we should report a diagnostic
        if (!isPartial)
        {
            // Report invalid XML diagnostic only if the method would have generated content
            if (hasInvalidXml)
            {
                // We can't know if it would have been generatable, so skip for non-partial
                return MethodToGenerate.Empty;
            }

            // Check if this non-partial method has generatable content - if so, report diagnostic
            if (xmlDocs is not null && descriptionAttribute is not null && 
                HasGeneratableContent(xmlDocs, methodSymbol, descriptionAttribute))
            {
                return MethodToGenerate.CreateDiagnosticOnly(
                    DiagnosticInfo.Create("MCP002", methodDeclaration.Identifier.GetLocation(), methodSymbol.Name));
            }

            return MethodToGenerate.Empty;
        }

        // For partial methods with invalid XML, report diagnostic but still generate partial declaration.
        EquatableArray<DiagnosticInfo> diagnostics = hasInvalidXml ?
            new EquatableArray<DiagnosticInfo>(ImmutableArray.Create(DiagnosticInfo.Create("MCP001", methodSymbol.Locations.FirstOrDefault(), methodSymbol.Name))) :
            default;

        bool needsMethodDescription = xmlDocs is not null &&
            !string.IsNullOrWhiteSpace(xmlDocs.MethodDescription) &&
            (descriptionAttribute is null || !HasAttribute(methodSymbol, descriptionAttribute));

        bool needsReturnDescription = xmlDocs is not null &&
            !string.IsNullOrWhiteSpace(xmlDocs.Returns) &&
            (descriptionAttribute is null ||
             methodSymbol.GetReturnTypeAttributes().All(attr => !SymbolEqualityComparer.Default.Equals(attr.AttributeClass, descriptionAttribute)));

        // Extract method info for partial methods
        var modifiers = methodDeclaration.Modifiers
            .Where(m => !m.IsKind(SyntaxKind.AsyncKeyword))
            .Select(m => m.Text);
        string modifiersStr = string.Join(" ", modifiers);
        string returnType = methodSymbol.ReturnType.ToDisplayString(s_fullyQualifiedFormatWithNullability);
        string methodName = methodSymbol.Name;

        // Extract parameters
        var parameterSyntaxList = methodDeclaration.ParameterList.Parameters;
        ParameterInfo[] parameters = new ParameterInfo[methodSymbol.Parameters.Length];
        for (int i = 0; i < methodSymbol.Parameters.Length; i++)
        {
            var param = methodSymbol.Parameters[i];
            var paramSyntax = i < parameterSyntaxList.Count ? parameterSyntaxList[i] : null;

            parameters[i] = new ParameterInfo(
                ParameterType: param.Type.ToDisplayString(s_fullyQualifiedFormatWithNullability),
                Name: param.Name,
                HasDescriptionAttribute: descriptionAttribute is not null && HasAttribute(param, descriptionAttribute),
                XmlDescription: xmlDocs?.Parameters.TryGetValue(param.Name, out var pd) == true && !string.IsNullOrWhiteSpace(pd) ? pd : null,
                DefaultValue: paramSyntax?.Default?.ToFullString().Trim());
        }

        return new MethodToGenerate(
            NeedsGeneration: true,
            TypeInfo: ExtractTypeInfo(methodSymbol.ContainingType),
            Modifiers: modifiersStr,
            ReturnType: returnType,
            MethodName: methodName,
            Parameters: new EquatableArray<ParameterInfo>(parameters),
            MethodDescription: needsMethodDescription ? xmlDocs?.MethodDescription : null,
            ReturnDescription: needsReturnDescription ? xmlDocs?.Returns : null,
            Diagnostics: diagnostics);
    }

    /// <summary>Checks if XML documentation would generate any Description attributes for a method.</summary>
    private static bool HasGeneratableContent(XmlDocumentation xmlDocs, IMethodSymbol methodSymbol, INamedTypeSymbol descriptionAttribute)
    {
        if (!string.IsNullOrWhiteSpace(xmlDocs.MethodDescription) && !HasAttribute(methodSymbol, descriptionAttribute))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(xmlDocs.Returns) &&
            methodSymbol.GetReturnTypeAttributes().All(attr => !SymbolEqualityComparer.Default.Equals(attr.AttributeClass, descriptionAttribute)))
        {
            return true;
        }

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

    private static TypeInfo ExtractTypeInfo(INamedTypeSymbol? typeSymbol)
    {
        if (typeSymbol is null)
        {
            return new TypeInfo(string.Empty, default);
        }

        // Build list of nested types from innermost to outermost
        var typesBuilder = ImmutableArray.CreateBuilder<TypeDeclarationInfo>();
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

            typesBuilder.Add(new TypeDeclarationInfo(current.Name, typeKeyword));
        }

        // Reverse to get outermost first
        typesBuilder.Reverse();
        
        string ns = typeSymbol.ContainingNamespace.IsGlobalNamespace ? "" : typeSymbol.ContainingNamespace.ToDisplayString();
        return new TypeInfo(ns, new EquatableArray<TypeDeclarationInfo>(typesBuilder.ToImmutable()));
    }

    private static (XmlDocumentation? Docs, bool HasInvalidXml) TryExtractXmlDocumentation(IMethodSymbol methodSymbol)
    {
        string? xmlDoc = methodSymbol.GetDocumentationCommentXml();
        if (string.IsNullOrWhiteSpace(xmlDoc))
        {
            return (null, false);
        }

        try
        {
            if (XDocument.Parse(xmlDoc).Element("member") is not { } memberElement)
            {
                return (null, false);
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
            return (new XmlDocumentation(methodDescription ?? string.Empty, returns ?? string.Empty, paramDocs), false);
        }
        catch (XmlException)
        {
            return (null, true);
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

    private static string GenerateSourceFile(EquatableArray<MethodToGenerate> methods)
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

            // Preserve default parameter values
            if (!string.IsNullOrEmpty(param.DefaultValue))
            {
                writer.Write(' ');
                writer.Write(param.DefaultValue);
            }
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

    /// <summary>Represents a method that may need Description attributes generated.</summary>
    private readonly record struct MethodToGenerate(
        bool NeedsGeneration,
        TypeInfo TypeInfo,
        string Modifiers,
        string ReturnType,
        string MethodName,
        EquatableArray<ParameterInfo> Parameters,
        string? MethodDescription,
        string? ReturnDescription,
        EquatableArray<DiagnosticInfo> Diagnostics) : IEquatable<MethodToGenerate>
    {
        public static MethodToGenerate Empty => new(
            NeedsGeneration: false,
            TypeInfo: default,
            Modifiers: string.Empty,
            ReturnType: string.Empty,
            MethodName: string.Empty,
            Parameters: default,
            MethodDescription: null,
            ReturnDescription: null,
            Diagnostics: default);

        public static MethodToGenerate CreateDiagnosticOnly(DiagnosticInfo diagnostic) => new(
            NeedsGeneration: false,
            TypeInfo: default,
            Modifiers: string.Empty,
            ReturnType: string.Empty,
            MethodName: string.Empty,
            Parameters: default,
            MethodDescription: null,
            ReturnDescription: null,
            Diagnostics: new([diagnostic]));
    }

    /// <summary>Holds information about a method parameter.</summary>
    private readonly record struct ParameterInfo(
        string ParameterType,
        string Name,
        bool HasDescriptionAttribute,
        string? XmlDescription,
        string? DefaultValue);

    /// <summary>Holds information about a type containing MCP methods.</summary>
    private readonly record struct TypeInfo(
        string Namespace,
        EquatableArray<TypeDeclarationInfo> Types);

    /// <summary>Holds information about a type declaration.</summary>
    private readonly record struct TypeDeclarationInfo(
        string Name,
        string TypeKeyword);

    /// <summary>Holds serializable location information for incremental generator caching.</summary>
    /// <remarks>
    /// Roslyn <see cref="Location"/> objects cannot be stored in incremental generator cached data
    /// because they contain references to syntax trees from specific compilations. Storing them
    /// causes issues when the generator returns cached data with locations from earlier compilations.
    /// </remarks>
    private readonly record struct LocationInfo(string FilePath, TextSpan TextSpan, LinePositionSpan LineSpan)
    {
        public static LocationInfo? FromLocation(Location? location) =>
            location is null || !location.IsInSource ? null :
            new LocationInfo(location.SourceTree?.FilePath ?? "", location.SourceSpan, location.GetLineSpan().Span);

        public Location ToLocation() =>
            Location.Create(FilePath, TextSpan, LineSpan);
    }

    /// <summary>Holds diagnostic information to be reported.</summary>
    private readonly record struct DiagnosticInfo(string Id, LocationInfo? Location, string MethodName)
    {
        public static DiagnosticInfo Create(string id, Location? location, string methodName) =>
            new(id, LocationInfo.FromLocation(location), methodName);

        public object?[] MessageArgs => [MethodName];
    }

    /// <summary>Holds extracted XML documentation for a method (used only during extraction, not cached).</summary>
    private sealed record XmlDocumentation(string MethodDescription, string Returns, Dictionary<string, string> Parameters);
}
