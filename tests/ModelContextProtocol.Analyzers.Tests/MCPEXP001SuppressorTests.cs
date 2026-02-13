using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using System.Collections.Immutable;
using Xunit;

namespace ModelContextProtocol.Analyzers.Tests;

public class MCPEXP001SuppressorTests
{
    [Fact]
    public async Task Suppressor_InGeneratedCode_SuppressesMCPEXP001()
    {
        // Simulate source-generated code (e.g., STJ source gen) that references an experimental type.
        // The file path ends with .g.cs to indicate it's generated.
        var result = await RunSuppressorAsync(
            source: """
                using ExperimentalTypes;

                namespace Generated
                {
                    public static class SerializerHelper
                    {
                        public static object Create() => new ExperimentalClass();
                    }
                }
                """,
            filePath: "Generated.g.cs",
            additionalSource: GetExperimentalTypeDefinition(),
            additionalFilePath: "ExperimentalTypes.cs");

        // MCPEXP001 should exist before the suppressor runs
        Assert.Contains(result.BeforeSuppression, d => d.Id == "MCPEXP001");

        // After suppression, MCPEXP001 should be gone from the results
        Assert.DoesNotContain(result.AfterSuppression, d => d.Id == "MCPEXP001");
    }

    [Fact]
    public async Task Suppressor_InHandWrittenCode_DoesNotSuppressMCPEXP001()
    {
        // Hand-written user code referencing an experimental type.
        // The file path does NOT end with .g.cs.
        var result = await RunSuppressorAsync(
            source: """
                using ExperimentalTypes;

                namespace UserCode
                {
                    public static class MyHelper
                    {
                        public static object Create() => new ExperimentalClass();
                    }
                }
                """,
            filePath: "MyHelper.cs",
            additionalSource: GetExperimentalTypeDefinition(),
            additionalFilePath: "ExperimentalTypes.cs");

        // MCPEXP001 should exist before the suppressor runs
        Assert.Contains(result.BeforeSuppression, d => d.Id == "MCPEXP001");

        // It should still be present after the suppressor runs (not suppressed)
        Assert.Contains(result.AfterSuppression, d => d.Id == "MCPEXP001");
    }

    [Fact]
    public async Task Suppressor_MixedGeneratedAndHandWritten_OnlySuppressesGenerated()
    {
        var result = await RunSuppressorAsync(
            [
                (GetExperimentalTypeDefinition(), "ExperimentalTypes.cs"),
                ("""
                    using ExperimentalTypes;
                    namespace Generated
                    {
                        public static class GeneratedHelper
                        {
                            public static object Create() => new ExperimentalClass();
                        }
                    }
                    """, "Generated.g.cs"),
                ("""
                    using ExperimentalTypes;
                    namespace UserCode
                    {
                        public static class UserHelper
                        {
                            public static object Create() => new ExperimentalClass();
                        }
                    }
                    """, "UserCode.cs"),
            ]);

        // Should have MCPEXP001 in both files before suppression
        Assert.Equal(2, result.BeforeSuppression.Count(d => d.Id == "MCPEXP001"));

        // After suppression: only the hand-written one should remain
        var remaining = result.AfterSuppression.Where(d => d.Id == "MCPEXP001").ToList();
        Assert.Single(remaining);
        Assert.Equal("UserCode.cs", remaining[0].Location.SourceTree?.FilePath);
    }

    private static string GetExperimentalTypeDefinition() => """
        using System.Diagnostics.CodeAnalysis;

        namespace ExperimentalTypes
        {
            [Experimental("MCPEXP001")]
            public class ExperimentalClass { }
        }
        """;

    private static Task<SuppressorResult> RunSuppressorAsync(
        string source,
        string filePath,
        string additionalSource,
        string additionalFilePath)
    {
        return RunSuppressorAsync([(additionalSource, additionalFilePath), (source, filePath)]);
    }

    private static async Task<SuppressorResult> RunSuppressorAsync(params (string Source, string FilePath)[] sources)
    {
        var syntaxTrees = sources.Select(
            s => CSharpSyntaxTree.ParseText(s.Source, path: s.FilePath)).ToArray();

        var runtimePath = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        List<MetadataReference> referenceList =
        [
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(Path.Combine(runtimePath, "System.Runtime.dll")),
            MetadataReference.CreateFromFile(Path.Combine(runtimePath, "netstandard.dll")),
        ];

        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            syntaxTrees,
            referenceList,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var beforeSuppression = compilation.GetDiagnostics();
        var analyzers = ImmutableArray.Create<DiagnosticAnalyzer>(new MCPEXP001Suppressor());
        var compilationWithAnalyzers = compilation.WithAnalyzers(analyzers);
        var afterSuppression = await compilationWithAnalyzers.GetAllDiagnosticsAsync(default);

        return new SuppressorResult
        {
            BeforeSuppression = beforeSuppression,
            AfterSuppression = afterSuppression,
        };
    }

    private class SuppressorResult
    {
        public ImmutableArray<Diagnostic> BeforeSuppression { get; set; } = [];
        public ImmutableArray<Diagnostic> AfterSuppression { get; set; } = [];
    }
}
