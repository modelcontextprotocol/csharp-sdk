using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Collections.Immutable;
using System.Reflection;
using Xunit;

namespace ModelContextProtocol.SourceGenerators.Tests;

public class XmlToDescriptionGeneratorTests
{
    [Fact]
    public void Generator_WithSummaryOnly_GeneratesMethodDescription()
    {
        var source = """
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
            """;

        var result = RunGenerator(source);

        Assert.True(result.Success);
        Assert.Single(result.GeneratedSources);
        
        var generatedSource = result.GeneratedSources[0].SourceText.ToString();
        Assert.Contains("[Description(\"Test tool description\")]", generatedSource);
        Assert.Contains("public static partial string TestMethod", generatedSource);
    }

    [Fact]
    public void Generator_WithSummaryAndRemarks_CombinesInMethodDescription()
    {
        var source = """
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
            """;

        var result = RunGenerator(source);

        Assert.True(result.Success);
        var generatedSource = result.GeneratedSources[0].SourceText.ToString();
        Assert.Contains("[Description(\"Test tool summary Additional remarks\")]", generatedSource);
    }

    [Fact]
    public void Generator_WithParameterDocs_GeneratesParameterDescriptions()
    {
        var source = """
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
            """;

        var result = RunGenerator(source);

        Assert.True(result.Success);
        var generatedSource = result.GeneratedSources[0].SourceText.ToString();
        Assert.Contains("[Description(\"Input parameter description\")]", generatedSource);
        Assert.Contains("[Description(\"Count parameter description\")]", generatedSource);
    }

    [Fact]
    public void Generator_WithReturnDocs_GeneratesReturnDescription()
    {
        var source = """
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
            """;

        var result = RunGenerator(source);

        Assert.True(result.Success);
        var generatedSource = result.GeneratedSources[0].SourceText.ToString();
        Assert.Contains("[return: Description(\"The result of the operation\")]", generatedSource);
    }

    [Fact]
    public void Generator_WithExistingMethodDescription_DoesNotGenerateMethodDescription()
    {
        var source = """
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
            """;

        var result = RunGenerator(source);

        Assert.True(result.Success);
        var generatedSource = result.GeneratedSources[0].SourceText.ToString();
        // Should not contain method description, only return description
        Assert.DoesNotContain("Test tool summary", generatedSource);
        Assert.Contains("[return: Description(\"Result\")]", generatedSource);
    }

    [Fact]
    public void Generator_WithExistingParameterDescription_SkipsThatParameter()
    {
        var source = """
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
            """;

        var result = RunGenerator(source);

        Assert.True(result.Success);
        var generatedSource = result.GeneratedSources[0].SourceText.ToString();
        // Should generate description for input but not count
        Assert.Contains("[Description(\"Input description\")] string input", generatedSource);
        Assert.DoesNotContain("Count description", generatedSource);
    }

    [Fact]
    public void Generator_WithoutMcpServerToolAttribute_DoesNotGenerate()
    {
        var source = """
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
            """;

        var result = RunGenerator(source);

        Assert.True(result.Success);
        Assert.Empty(result.GeneratedSources);
    }

    [Fact]
    public void Generator_WithoutPartialKeyword_DoesNotGenerate()
    {
        var source = """
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
            """;

        var result = RunGenerator(source);

        Assert.True(result.Success);
        Assert.Empty(result.GeneratedSources);
    }

    [Fact]
    public void Generator_WithSpecialCharacters_EscapesCorrectly()
    {
        var source = """
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
            """;

        var result = RunGenerator(source);

        Assert.True(result.Success);
        Assert.Single(result.GeneratedSources);
        
        var generatedSource = result.GeneratedSources[0].SourceText.ToString();
        // Verify quotes are escaped
        Assert.Contains("\\\"quotes\\\"", generatedSource);
        // Verify backslashes are escaped
        Assert.Contains("\\\\backslash", generatedSource);
    }

    [Fact]
    public void Generator_WithInvalidXml_DoesNotThrow()
    {
        var source = """
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
            """;

        var result = RunGenerator(source);

        // Should not throw, just skip generation
        Assert.True(result.Success);
        Assert.Empty(result.GeneratedSources);
    }

    [Fact]
    public void Generator_WithGenericType_GeneratesCorrectFileName()
    {
        var source = """
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
            """;

        var result = RunGenerator(source);

        Assert.True(result.Success);
        Assert.Single(result.GeneratedSources);
        
        var fileName = result.GeneratedSources[0].FilePath;
        // Should include arity for generic types
        Assert.Contains("`1", fileName);
    }

    [Fact]
    public void Generator_WithEmptyXmlComments_DoesNotGenerate()
    {
        var source = """
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
            """;

        var result = RunGenerator(source);

        Assert.True(result.Success);
        Assert.Empty(result.GeneratedSources);
    }

    [Fact]
    public void Generator_WithMultilineComments_CombinesIntoSingleLine()
    {
        var source = """
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
            """;

        var result = RunGenerator(source);

        Assert.True(result.Success);
        Assert.Single(result.GeneratedSources);
        
        var generatedSource = result.GeneratedSources[0].SourceText.ToString();
        Assert.Contains("[Description(\"First line Second line Third line\")]", generatedSource);
    }

    private GeneratorRunResult RunGenerator(string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source);

        // Get reference assemblies - we need to include all the basic runtime types
        var referenceList = new List<MetadataReference>
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(System.ComponentModel.DescriptionAttribute).Assembly.Location),
        };

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
            new[] { syntaxTree },
            referenceList,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var generator = new XmlToDescriptionGenerator();

        var driver = CSharpGeneratorDriver.Create(generator);
        driver = (CSharpGeneratorDriver)driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var diagnostics);

        var runResult = driver.GetRunResult();

        return new GeneratorRunResult
        {
            Success = !diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error),
            GeneratedSources = runResult.GeneratedTrees.Select(t => (t.FilePath, t.GetText())).ToList(),
            Diagnostics = diagnostics.ToList(),
            Compilation = outputCompilation
        };
    }

    private class GeneratorRunResult
    {
        public bool Success { get; set; }
        public List<(string FilePath, Microsoft.CodeAnalysis.Text.SourceText SourceText)> GeneratedSources { get; set; } = new();
        public List<Diagnostic> Diagnostics { get; set; } = new();
        public Compilation? Compilation { get; set; }
    }
}
