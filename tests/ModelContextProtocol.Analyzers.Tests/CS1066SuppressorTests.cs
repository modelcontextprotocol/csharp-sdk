using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using System.Collections.Immutable;
using Xunit;

namespace ModelContextProtocol.Analyzers.Tests;

public class CS1066SuppressorTests
{
    [Fact]
    public void Suppressor_WithMcpServerToolAttribute_SuppressesCS1066()
    {
        var result = RunSuppressor("""
            using ModelContextProtocol.Server;

            namespace Test;

            [McpServerToolType]
            public partial class TestTools
            {
                [McpServerTool]
                public partial string TestMethod(string input = "default");
            }

            public partial class TestTools
            {
                public partial string TestMethod(string input = "default")
                {
                    return input;
                }
            }
            """);

        // CS1066 should be suppressed
        Assert.Empty(result.Diagnostics.Where(d => d.Id == "CS1066" && !d.IsSuppressed));
        Assert.Contains(result.Diagnostics, d => d.Id == "CS1066" && d.IsSuppressed);
    }

    [Fact]
    public void Suppressor_WithMcpServerPromptAttribute_SuppressesCS1066()
    {
        var result = RunSuppressor("""
            using ModelContextProtocol.Server;

            namespace Test;

            [McpServerPromptType]
            public partial class TestPrompts
            {
                [McpServerPrompt]
                public partial string TestPrompt(string input = "default");
            }

            public partial class TestPrompts
            {
                public partial string TestPrompt(string input = "default")
                {
                    return input;
                }
            }
            """);

        // CS1066 should be suppressed
        Assert.Empty(result.Diagnostics.Where(d => d.Id == "CS1066" && !d.IsSuppressed));
        Assert.Contains(result.Diagnostics, d => d.Id == "CS1066" && d.IsSuppressed);
    }

    [Fact]
    public void Suppressor_WithMcpServerResourceAttribute_SuppressesCS1066()
    {
        var result = RunSuppressor("""
            using ModelContextProtocol.Server;

            namespace Test;

            [McpServerResourceType]
            public partial class TestResources
            {
                [McpServerResource("test://resource")]
                public partial string TestResource(string input = "default");
            }

            public partial class TestResources
            {
                public partial string TestResource(string input = "default")
                {
                    return input;
                }
            }
            """);

        // CS1066 should be suppressed
        Assert.Empty(result.Diagnostics.Where(d => d.Id == "CS1066" && !d.IsSuppressed));
        Assert.Contains(result.Diagnostics, d => d.Id == "CS1066" && d.IsSuppressed);
    }

    [Fact]
    public void Suppressor_WithoutMcpAttribute_DoesNotSuppressCS1066()
    {
        var result = RunSuppressor("""
            namespace Test;

            public partial class TestTools
            {
                public partial string TestMethod(string input = "default");
            }

            public partial class TestTools
            {
                public partial string TestMethod(string input = "default")
                {
                    return input;
                }
            }
            """);

        // CS1066 should NOT be suppressed (no MCP attribute)
        Assert.Contains(result.Diagnostics, d => d.Id == "CS1066" && !d.IsSuppressed);
        Assert.Empty(result.Diagnostics.Where(d => d.Id == "CS1066" && d.IsSuppressed));
    }

    [Fact]
    public void Suppressor_WithMultipleParameters_SuppressesAllCS1066()
    {
        var result = RunSuppressor("""
            using ModelContextProtocol.Server;

            namespace Test;

            [McpServerToolType]
            public partial class TestTools
            {
                [McpServerTool]
                public partial string TestMethod(string input = "default", int count = 42, bool flag = false);
            }

            public partial class TestTools
            {
                public partial string TestMethod(string input = "default", int count = 42, bool flag = false)
                {
                    return input;
                }
            }
            """);

        // All CS1066 warnings should be suppressed
        var cs1066Diagnostics = result.Diagnostics.Where(d => d.Id == "CS1066").ToList();
        Assert.Equal(3, cs1066Diagnostics.Count); // Three parameters with defaults
        Assert.All(cs1066Diagnostics, d => Assert.True(d.IsSuppressed));
    }

    private SuppressorResult RunSuppressor(string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source);

        // Get reference assemblies
        List<MetadataReference> referenceList =
        [
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(System.ComponentModel.DescriptionAttribute).Assembly.Location),
        ];

        // Add all necessary runtime assemblies
        var runtimePath = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        referenceList.Add(MetadataReference.CreateFromFile(Path.Combine(runtimePath, "System.Runtime.dll")));
        referenceList.Add(MetadataReference.CreateFromFile(Path.Combine(runtimePath, "netstandard.dll")));

        // Add ModelContextProtocol.Core
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

        // Run the suppressor
        var analyzers = ImmutableArray.Create<DiagnosticAnalyzer>(new CS1066Suppressor());

        var compilationWithAnalyzers = compilation.WithAnalyzers(analyzers);
        var diagnostics = compilationWithAnalyzers.GetAllDiagnosticsAsync().GetAwaiter().GetResult();

        return new SuppressorResult
        {
            Diagnostics = diagnostics.ToList()
        };
    }

    private class SuppressorResult
    {
        public List<Diagnostic> Diagnostics { get; set; } = [];
    }
}
