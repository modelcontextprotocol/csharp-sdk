using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Diagnostics.CodeAnalysis;
using Xunit;

namespace ModelContextProtocol.Analyzers.Tests;

public partial class XmlToDescriptionGeneratorTests
{
    [Fact]
    public void Generator_WithSummaryOnly_GeneratesMethodDescription()
    {
        var result = RunGenerator("""
            using ModelContextProtocol.Server;
            using System.ComponentModel;

            namespace Test;

            [McpServerToolType]
            public partial class TestTools
            {
                /// <summary>
                /// Test tool description
                /// </summary>
                [McpServerTool]
                public static partial string TestMethod(string input)
                {
                    return input;
                }
            }
            """);

        Assert.True(result.Success);
        Assert.Single(result.GeneratedSources);
        
        var generatedSource = result.GeneratedSources[0].SourceText.ToString();
        Assert.Contains("[Description(\"Test tool description\")]", generatedSource);
        Assert.Contains("public static partial string TestMethod", generatedSource);
    }

    [Fact]
    public void Generator_WithSummaryAndRemarks_CombinesInMethodDescription()
    {
        var result = RunGenerator("""
            using ModelContextProtocol.Server;
            using System.ComponentModel;

            namespace Test;

            [McpServerToolType]
            public partial class TestTools
            {
                /// <summary>
                /// Test tool summary
                /// </summary>
                /// <remarks>
                /// Additional remarks
                /// </remarks>
                [McpServerTool]
                public static partial string TestMethod(string input)
                {
                    return input;
                }
            }
            """);

        Assert.True(result.Success);
        var generatedSource = result.GeneratedSources[0].SourceText.ToString();
        Assert.Contains("[Description(\"Test tool summary\\nAdditional remarks\")]", generatedSource);
    }

    [Fact]
    public void Generator_WithParameterDocs_GeneratesParameterDescriptions()
    {
        var result = RunGenerator("""
            using ModelContextProtocol.Server;
            using System.ComponentModel;

            namespace Test;

            [McpServerToolType]
            public partial class TestTools
            {
                /// <summary>
                /// Test tool
                /// </summary>
                /// <param name="input">Input parameter description</param>
                /// <param name="count">Count parameter description</param>
                [McpServerTool]
                public static partial string TestMethod(string input, int count)
                {
                    return input;
                }
            }
            """);

        Assert.True(result.Success);
        var generatedSource = result.GeneratedSources[0].SourceText.ToString();
        Assert.Contains("[Description(\"Input parameter description\")]", generatedSource);
        Assert.Contains("[Description(\"Count parameter description\")]", generatedSource);
    }

    [Fact]
    public void Generator_WithReturnDocs_GeneratesReturnDescription()
    {
        var result = RunGenerator("""
            using ModelContextProtocol.Server;
            using System.ComponentModel;

            namespace Test;

            [McpServerToolType]
            public partial class TestTools
            {
                /// <summary>
                /// Test tool
                /// </summary>
                /// <returns>The result of the operation</returns>
                [McpServerTool]
                public static partial string TestMethod(string input)
                {
                    return input;
                }
            }
            """);

        Assert.True(result.Success);
        var generatedSource = result.GeneratedSources[0].SourceText.ToString();
        Assert.Contains("[return: Description(\"The result of the operation\")]", generatedSource);
    }

    [Fact]
    public void Generator_WithExistingMethodDescription_DoesNotGenerateMethodDescription()
    {
        var result = RunGenerator("""
            using ModelContextProtocol.Server;
            using System.ComponentModel;

            namespace Test;

            [McpServerToolType]
            public partial class TestTools
            {
                /// <summary>
                /// Test tool summary
                /// </summary>
                /// <returns>Result</returns>
                [McpServerTool]
                [Description("Already has description")]
                public static partial string TestMethod(string input)
                {
                    return input;
                }
            }
            """);

        Assert.True(result.Success);
        var generatedSource = result.GeneratedSources[0].SourceText.ToString();
        // Should not contain method description, only return description
        Assert.DoesNotContain("Test tool summary", generatedSource);
        Assert.Contains("[return: Description(\"Result\")]", generatedSource);
    }

    [Fact]
    public void Generator_WithExistingParameterDescription_SkipsThatParameter()
    {
        var result = RunGenerator("""
            using ModelContextProtocol.Server;
            using System.ComponentModel;

            namespace Test;

            [McpServerToolType]
            public partial class TestTools
            {
                /// <summary>
                /// Test tool
                /// </summary>
                /// <param name="input">Input description</param>
                /// <param name="count">Count description</param>
                [McpServerTool]
                public static partial string TestMethod(string input, [Description("Already has")] int count)
                {
                    return input;
                }
            }
            """);

        Assert.True(result.Success);
        var generatedSource = result.GeneratedSources[0].SourceText.ToString();
        // Should generate description for input but not count
        Assert.Contains("[Description(\"Input description\")] string input", generatedSource);
        Assert.DoesNotContain("Count description", generatedSource);
    }

    [Fact]
    public void Generator_WithoutMcpServerToolAttribute_DoesNotGenerate()
    {
        var result = RunGenerator("""
            using ModelContextProtocol.Server;
            using System.ComponentModel;

            namespace Test;

            public partial class TestTools
            {
                /// <summary>
                /// Test tool
                /// </summary>
                public static partial string TestMethod(string input)
                {
                    return input;
                }
            }
            """);

        Assert.True(result.Success);
        Assert.Empty(result.GeneratedSources);
    }

    [Fact]
    public void Generator_WithoutPartialKeyword_DoesNotGenerate()
    {
        var result = RunGenerator("""
            using ModelContextProtocol.Server;
            using System.ComponentModel;

            namespace Test;

            [McpServerToolType]
            public class TestTools
            {
                /// <summary>
                /// Test tool
                /// </summary>
                [McpServerTool]
                public static string TestMethod(string input)
                {
                    return input;
                }
            }
            """);

        Assert.True(result.Success);
        Assert.Empty(result.GeneratedSources);
    }

    [Fact]
    public void Generator_WithSpecialCharacters_EscapesCorrectly()
    {
        var result = RunGenerator("""
            using ModelContextProtocol.Server;
            using System.ComponentModel;

            namespace Test;

            [McpServerToolType]
            public partial class TestTools
            {
                /// <summary>
                /// Test with "quotes", \backslash, newline
                /// and tab characters.
                /// </summary>
                /// <param name="input">Parameter with "quotes"</param>
                [McpServerTool]
                public static partial string TestEscaping(string input)
                {
                    return input;
                }
            }
            """);

        Assert.True(result.Success);
        Assert.Single(result.GeneratedSources);
        
        var generatedSource = result.GeneratedSources[0].SourceText.ToString();
        // Verify quotes are escaped
        Assert.Contains("\\\"quotes\\\"", generatedSource);
        // Verify backslashes are escaped
        Assert.Contains("\\\\backslash", generatedSource);
    }

    [Fact]
    public void Generator_WithInvalidXml_GeneratesPartialAndReportsDiagnostic()
    {
        var result = RunGenerator("""
            using ModelContextProtocol.Server;
            using System.ComponentModel;

            namespace Test;

            [McpServerToolType]
            public partial class TestTools
            {
                /// <summary>
                /// Test with <unclosed tag
                /// </summary>
                [McpServerTool]
                public static partial string TestInvalidXml(string input)
                {
                    return input;
                }
            }
            """);

        // Should not throw, generates partial implementation without Description attributes
        Assert.True(result.Success);
        Assert.Single(result.GeneratedSources);
        
        var generatedSource = result.GeneratedSources[0].SourceText.ToString();
        // Should generate the partial implementation
        Assert.Contains("public static partial string TestInvalidXml", generatedSource);
        // Should NOT contain Description attribute since XML was invalid
        Assert.DoesNotContain("[Description(", generatedSource);
        
        // Should report a warning diagnostic
        var diagnostic = Assert.Single(result.Diagnostics, d => d.Id == "MCP001");
        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.Contains("invalid", diagnostic.GetMessage(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Generator_WithGenericType_GeneratesCorrectly()
    {
        var result = RunGenerator("""
            using ModelContextProtocol.Server;
            using System.ComponentModel;

            namespace Test;

            [McpServerToolType]
            public partial class TestTools<T>
            {
                /// <summary>
                /// Test generic
                /// </summary>
                [McpServerTool]
                public static partial string TestGeneric(string input)
                {
                    return input;
                }
            }
            """);

        Assert.True(result.Success);
        Assert.Single(result.GeneratedSources);
        
        var generatedSource = result.GeneratedSources[0].SourceText.ToString();
        Assert.Contains("[Description(\"Test generic\")]", generatedSource);
    }

    [Fact]
    public void Generator_WithEmptyXmlComments_GeneratesPartialWithoutDescription()
    {
        var result = RunGenerator("""
            using ModelContextProtocol.Server;
            using System.ComponentModel;

            namespace Test;

            [McpServerToolType]
            public partial class TestTools
            {
                /// <summary>
                /// </summary>
                [McpServerTool]
                public static partial string TestEmpty(string input)
                {
                    return input;
                }
            }
            """);

        Assert.True(result.Success);
        Assert.Single(result.GeneratedSources);
        
        var generatedSource = result.GeneratedSources[0].SourceText.ToString();
        // Should generate the partial implementation
        Assert.Contains("public static partial string TestEmpty", generatedSource);
        // Should NOT contain Description attribute since documentation was empty
        Assert.DoesNotContain("[Description(", generatedSource);
    }

    [Fact]
    public void Generator_WithMultilineComments_CombinesIntoSingleLine()
    {
        var result = RunGenerator("""
            using ModelContextProtocol.Server;
            using System.ComponentModel;

            namespace Test;

            [McpServerToolType]
            public partial class TestTools
            {
                /// <summary>
                /// First line
                /// Second line
                /// Third line
                /// </summary>
                [McpServerTool]
                public static partial string TestMultiline(string input)
                {
                    return input;
                }
            }
            """);

        Assert.True(result.Success);
        Assert.Single(result.GeneratedSources);
        
        var generatedSource = result.GeneratedSources[0].SourceText.ToString();
        Assert.Contains("[Description(\"First line Second line Third line\")]", generatedSource);
    }

    [Fact]
    public void Generator_WithParametersOnly_GeneratesParameterDescriptions()
    {
        var result = RunGenerator("""
            using ModelContextProtocol.Server;
            using System.ComponentModel;

            namespace Test;

            [McpServerToolType]
            public partial class TestTools
            {
                /// <param name="input">Input parameter</param>
                /// <param name="count">Count parameter</param>
                [McpServerTool]
                public static partial string TestMethod(string input, int count)
                {
                    return input;
                }
            }
            """);

        Assert.True(result.Success);
        Assert.Single(result.GeneratedSources);
        
        var generatedSource = result.GeneratedSources[0].SourceText.ToString();
        Assert.Contains("[Description(\"Input parameter\")]", generatedSource);
        Assert.Contains("[Description(\"Count parameter\")]", generatedSource);
    }

    [Fact]
    public void Generator_WithNestedType_GeneratesCorrectly()
    {
        var result = RunGenerator("""
            using ModelContextProtocol.Server;
            using System.ComponentModel;

            namespace Test;

            public partial class OuterClass
            {
                [McpServerToolType]
                public partial class InnerClass
                {
                    /// <summary>
                    /// Nested tool
                    /// </summary>
                    [McpServerTool]
                    public static partial string NestedMethod(string input)
                    {
                        return input;
                    }
                }
            }
            """);

        Assert.True(result.Success);
        Assert.Single(result.GeneratedSources);
        
        var generatedSource = result.GeneratedSources[0].SourceText.ToString();
        Assert.Contains("partial class OuterClass", generatedSource);
        Assert.Contains("partial class InnerClass", generatedSource);
        Assert.Contains("[Description(\"Nested tool\")]", generatedSource);
    }

    [Fact]
    public void Generator_WithRecordClass_GeneratesCorrectly()
    {
        var result = RunGenerator("""
            using ModelContextProtocol.Server;
            using System.ComponentModel;

            namespace Test;

            [McpServerToolType]
            public partial record TestTools
            {
                /// <summary>
                /// Record tool
                /// </summary>
                [McpServerTool]
                public static partial string RecordMethod(string input)
                {
                    return input;
                }
            }
            """);

        Assert.True(result.Success);
        Assert.Single(result.GeneratedSources);
        
        var generatedSource = result.GeneratedSources[0].SourceText.ToString();
        // Records are generated with "record class" keyword
        Assert.Contains("partial record class TestTools", generatedSource);
        Assert.Contains("[Description(\"Record tool\")]", generatedSource);
    }

    [Fact]
    public void Generator_WithRecordStruct_GeneratesCorrectly()
    {
        var result = RunGenerator("""
            using ModelContextProtocol.Server;
            using System.ComponentModel;

            namespace Test;

            [McpServerToolType]
            public partial record struct TestTools
            {
                /// <summary>
                /// Record struct tool
                /// </summary>
                [McpServerTool]
                public static partial string RecordStructMethod(string input)
                {
                    return input;
                }
            }
            """);

        Assert.True(result.Success);
        Assert.Single(result.GeneratedSources);
        
        var generatedSource = result.GeneratedSources[0].SourceText.ToString();
        Assert.Contains("partial record struct TestTools", generatedSource);
        Assert.Contains("[Description(\"Record struct tool\")]", generatedSource);
    }

    [Fact]
    public void Generator_WithVirtualMethod_GeneratesCorrectly()
    {
        var result = RunGenerator("""
            using ModelContextProtocol.Server;
            using System.ComponentModel;

            namespace Test;

            [McpServerToolType]
            public partial class TestTools
            {
                /// <summary>
                /// Virtual tool
                /// </summary>
                [McpServerTool]
                public virtual partial string VirtualMethod(string input)
                {
                    return input;
                }
            }
            """);

        Assert.True(result.Success);
        Assert.Single(result.GeneratedSources);
        
        var generatedSource = result.GeneratedSources[0].SourceText.ToString();
        Assert.Contains("public virtual partial string VirtualMethod", generatedSource);
    }

    [Fact]
    public void Generator_WithAbstractMethod_GeneratesCorrectly()
    {
        var result = RunGenerator("""
            using ModelContextProtocol.Server;
            using System.ComponentModel;

            namespace Test;

            [McpServerToolType]
            public abstract partial class TestTools
            {
                /// <summary>
                /// Abstract tool
                /// </summary>
                [McpServerTool]
                public abstract partial string AbstractMethod(string input);
            }
            """);

        Assert.True(result.Success);
        Assert.Single(result.GeneratedSources);
        
        var generatedSource = result.GeneratedSources[0].SourceText.ToString();
        Assert.Contains("public abstract partial string AbstractMethod", generatedSource);
    }

    [Fact]
    public void Generator_WithMcpServerPrompt_GeneratesCorrectly()
    {
        var result = RunGenerator("""
            using ModelContextProtocol.Server;
            using System.ComponentModel;

            namespace Test;

            [McpServerPromptType]
            public partial class TestPrompts
            {
                /// <summary>
                /// Test prompt
                /// </summary>
                [McpServerPrompt]
                public static partial string TestPrompt(string input)
                {
                    return input;
                }
            }
            """);

        Assert.True(result.Success);
        Assert.Single(result.GeneratedSources);
        
        var generatedSource = result.GeneratedSources[0].SourceText.ToString();
        Assert.Contains("[Description(\"Test prompt\")]", generatedSource);
    }

    [Fact]
    public void Generator_WithMcpServerResource_GeneratesCorrectly()
    {
        var result = RunGenerator("""
            using ModelContextProtocol.Server;
            using System.ComponentModel;

            namespace Test;

            [McpServerResourceType]
            public partial class TestResources
            {
                /// <summary>
                /// Test resource
                /// </summary>
                [McpServerResource("test://resource")]
                public static partial string TestResource(string input)
                {
                    return input;
                }
            }
            """);

        Assert.True(result.Success);
        Assert.Single(result.GeneratedSources);
        
        var generatedSource = result.GeneratedSources[0].SourceText.ToString();
        Assert.Contains("[Description(\"Test resource\")]", generatedSource);
    }

    private GeneratorRunResult RunGenerator([StringSyntax("C#-test")] string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source);

        // Get reference assemblies - we need to include all the basic runtime types
        List<MetadataReference> referenceList =
        [
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(System.ComponentModel.DescriptionAttribute).Assembly.Location),
        ];

        // Add all necessary runtime assemblies
        var runtimePath = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        referenceList.Add(MetadataReference.CreateFromFile(Path.Combine(runtimePath, "System.Runtime.dll")));
        referenceList.Add(MetadataReference.CreateFromFile(Path.Combine(runtimePath, "netstandard.dll")));

        // Try to find and add ModelContextProtocol.Core
        try
        {
            var coreAssemblyPath = Path.Combine(AppContext.BaseDirectory, "ModelContextProtocol.Core.dll");
            if (File.Exists(coreAssemblyPath))
            {
                referenceList.Add(MetadataReference.CreateFromFile(coreAssemblyPath));
            }
        }
        catch
        {
            // If we can't find it, the compilation will fail with appropriate errors
        }

        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            [syntaxTree],
            referenceList,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var driver = (CSharpGeneratorDriver)CSharpGeneratorDriver
            .Create(new XmlToDescriptionGenerator())
            .RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var diagnostics);

        var runResult = driver.GetRunResult();

        return new GeneratorRunResult
        {
            Success = !diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error),
            GeneratedSources = runResult.GeneratedTrees.Select(t => (t.FilePath, t.GetText())).ToList(),
            Diagnostics = diagnostics.ToList(),
            Compilation = outputCompilation
        };
    }

    [Fact]
    public void Generator_WithGlobalNamespace_GeneratesCorrectly()
    {
        var result = RunGenerator("""
            using ModelContextProtocol.Server;
            using System.ComponentModel;

            [McpServerToolType]
            public partial class GlobalTools
            {
                /// <summary>
                /// Tool in global namespace
                /// </summary>
                [McpServerTool]
                public static partial string GlobalMethod(string input)
                {
                    return input;
                }
            }
            """);

        Assert.True(result.Success);
        Assert.Single(result.GeneratedSources);
        
        var generatedSource = result.GeneratedSources[0].SourceText.ToString();
        
        // Should not have a namespace declaration
        Assert.DoesNotContain("namespace ", generatedSource);
        // Should have the class at the root level
        Assert.Contains("partial class GlobalTools", generatedSource);
        Assert.Contains("[Description(\"Tool in global namespace\")]", generatedSource);
    }

    private class GeneratorRunResult
    {
        public bool Success { get; set; }
        public List<(string FilePath, Microsoft.CodeAnalysis.Text.SourceText SourceText)> GeneratedSources { get; set; } = [];
        public List<Diagnostic> Diagnostics { get; set; } = [];
        public Compilation? Compilation { get; set; }
    }
}
