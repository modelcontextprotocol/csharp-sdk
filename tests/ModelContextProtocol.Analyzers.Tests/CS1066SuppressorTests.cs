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

        // Check we have the CS1066 diagnostics from compiler
        var cs1066FromCompiler = result.CompilerDiagnostics.Where(d => d.Id == "CS1066").ToList();
        
        // CS1066 should be suppressed in the final diagnostics
        var unsuppressedCs1066 = result.Diagnostics.Where(d => d.Id == "CS1066" && !d.IsSuppressed).ToList();
        var suppressedCs1066 = result.Diagnostics.Where(d => d.Id == "CS1066" && d.IsSuppressed).ToList();

        Assert.True(cs1066FromCompiler.Count > 0 || suppressedCs1066.Count > 0, 
            $"Expected CS1066 diagnostics. Compiler diagnostics: {string.Join(", ", result.CompilerDiagnostics.Select(d => d.Id))}");
        Assert.Empty(unsuppressedCs1066);
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

        // Check we have the CS1066 diagnostics from compiler
        var cs1066FromCompiler = result.CompilerDiagnostics.Where(d => d.Id == "CS1066").ToList();
        
        // CS1066 should be suppressed in the final diagnostics
        var unsuppressedCs1066 = result.Diagnostics.Where(d => d.Id == "CS1066" && !d.IsSuppressed).ToList();
        var suppressedCs1066 = result.Diagnostics.Where(d => d.Id == "CS1066" && d.IsSuppressed).ToList();

        Assert.True(cs1066FromCompiler.Count > 0 || suppressedCs1066.Count > 0, 
            $"Expected CS1066 diagnostics. Compiler diagnostics: {string.Join(", ", result.CompilerDiagnostics.Select(d => d.Id))}");
        Assert.Empty(unsuppressedCs1066);
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

        // Check we have the CS1066 diagnostics from compiler
        var cs1066FromCompiler = result.CompilerDiagnostics.Where(d => d.Id == "CS1066").ToList();
        
        // CS1066 should be suppressed in the final diagnostics
        var unsuppressedCs1066 = result.Diagnostics.Where(d => d.Id == "CS1066" && !d.IsSuppressed).ToList();
        var suppressedCs1066 = result.Diagnostics.Where(d => d.Id == "CS1066" && d.IsSuppressed).ToList();

        Assert.True(cs1066FromCompiler.Count > 0 || suppressedCs1066.Count > 0, 
            $"Expected CS1066 diagnostics. Compiler diagnostics: {string.Join(", ", result.CompilerDiagnostics.Select(d => d.Id))}");
        Assert.Empty(unsuppressedCs1066);
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
        // Check we have the CS1066 diagnostic from compiler
        var cs1066FromCompiler = result.CompilerDiagnostics.Where(d => d.Id == "CS1066").ToList();
        Assert.NotEmpty(cs1066FromCompiler);
        
        // It should NOT be suppressed in the final diagnostics (still present as unsuppressed)
        var unsuppressedCs1066 = result.Diagnostics.Where(d => d.Id == "CS1066" && !d.IsSuppressed).ToList();
        Assert.NotEmpty(unsuppressedCs1066);
        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "CS1066" && d.IsSuppressed);
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

        // Check we have CS1066 diagnostics from compiler (one per parameter with default)
        var cs1066FromCompiler = result.CompilerDiagnostics.Where(d => d.Id == "CS1066").ToList();
        Assert.Equal(3, cs1066FromCompiler.Count); // Three parameters with defaults

        // All CS1066 warnings should be suppressed
        var unsuppressedCs1066 = result.Diagnostics.Where(d => d.Id == "CS1066" && !d.IsSuppressed).ToList();
        Assert.Empty(unsuppressedCs1066);
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

        // Add ModelContextProtocol.Core if available
        var coreAssemblyPath = Path.Combine(AppContext.BaseDirectory, "ModelContextProtocol.Core.dll");
        if (File.Exists(coreAssemblyPath))
        {
            referenceList.Add(MetadataReference.CreateFromFile(coreAssemblyPath));
        }

        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            [syntaxTree],
            referenceList,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        // Get compilation diagnostics first (includes CS1066)
        var compilerDiagnostics = compilation.GetDiagnostics();

        // Run the suppressor
        var analyzers = ImmutableArray.Create<DiagnosticAnalyzer>(new CS1066Suppressor());
        var compilationWithAnalyzers = compilation.WithAnalyzers(analyzers);
        var allDiagnostics = compilationWithAnalyzers.GetAllDiagnosticsAsync().GetAwaiter().GetResult();

        return new SuppressorResult
        {
            Diagnostics = allDiagnostics.ToList(),
            CompilerDiagnostics = compilerDiagnostics.ToList()
        };
    }

    private class SuppressorResult
    {
        public List<Diagnostic> Diagnostics { get; set; } = [];
        public List<Diagnostic> CompilerDiagnostics { get; set; } = [];
    }
}
