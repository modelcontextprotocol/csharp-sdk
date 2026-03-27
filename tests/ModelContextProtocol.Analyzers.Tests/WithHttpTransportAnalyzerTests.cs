using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using System.Collections.Immutable;
using Xunit;

namespace ModelContextProtocol.Analyzers.Tests;

public class WithHttpTransportAnalyzerTests
{
    // Stub types that mimic the real SDK types so the analyzer can resolve them.
    private const string StubTypes = """
        namespace ModelContextProtocol.Server
        {
            public interface IMcpServerBuilder { }
        }

        namespace ModelContextProtocol.AspNetCore
        {
            public class HttpServerTransportOptions
            {
                public bool Stateless { get; set; }
                public System.TimeSpan IdleTimeout { get; set; }
            }
        }

        namespace Microsoft.Extensions.DependencyInjection
        {
            using ModelContextProtocol.Server;
            using ModelContextProtocol.AspNetCore;

            public static class HttpMcpServerBuilderExtensions
            {
                public static IMcpServerBuilder WithHttpTransport(
                    this IMcpServerBuilder builder,
                    System.Action<HttpServerTransportOptions>? configureOptions = null)
                {
                    return builder;
                }
            }
        }
        """;

    [Fact]
    public void NoArguments_Reports()
    {
        var diagnostics = RunAnalyzer("""
            using Microsoft.Extensions.DependencyInjection;
            using ModelContextProtocol.Server;

            class Test
            {
                void Configure(IMcpServerBuilder builder)
                {
                    builder.WithHttpTransport();
                }
            }
            """);

        Assert.Single(diagnostics, d => d.Id == "MCP003");
    }

    [Fact]
    public void NullArgument_Reports()
    {
        var diagnostics = RunAnalyzer("""
            using Microsoft.Extensions.DependencyInjection;
            using ModelContextProtocol.Server;

            class Test
            {
                void Configure(IMcpServerBuilder builder)
                {
                    builder.WithHttpTransport(null);
                }
            }
            """);

        Assert.Single(diagnostics, d => d.Id == "MCP003");
    }

    [Fact]
    public void DefaultLiteral_Reports()
    {
        var diagnostics = RunAnalyzer("""
            using Microsoft.Extensions.DependencyInjection;
            using ModelContextProtocol.Server;
            using ModelContextProtocol.AspNetCore;
            using System;

            class Test
            {
                void Configure(IMcpServerBuilder builder)
                {
                    builder.WithHttpTransport(default(Action<HttpServerTransportOptions>));
                }
            }
            """);

        Assert.Single(diagnostics, d => d.Id == "MCP003");
    }

    [Fact]
    public void SimpleLambda_StatelessTrue_DoesNotReport()
    {
        var diagnostics = RunAnalyzer("""
            using Microsoft.Extensions.DependencyInjection;
            using ModelContextProtocol.Server;

            class Test
            {
                void Configure(IMcpServerBuilder builder)
                {
                    builder.WithHttpTransport(o => o.Stateless = true);
                }
            }
            """);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void SimpleLambda_StatelessFalse_DoesNotReport()
    {
        var diagnostics = RunAnalyzer("""
            using Microsoft.Extensions.DependencyInjection;
            using ModelContextProtocol.Server;

            class Test
            {
                void Configure(IMcpServerBuilder builder)
                {
                    builder.WithHttpTransport(o => o.Stateless = false);
                }
            }
            """);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void BlockLambda_StatelessSet_DoesNotReport()
    {
        var diagnostics = RunAnalyzer("""
            using Microsoft.Extensions.DependencyInjection;
            using ModelContextProtocol.Server;
            using System;

            class Test
            {
                void Configure(IMcpServerBuilder builder)
                {
                    builder.WithHttpTransport(options =>
                    {
                        options.Stateless = true;
                        options.IdleTimeout = TimeSpan.FromMinutes(30);
                    });
                }
            }
            """);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void BlockLambda_StatelessNotSet_Reports()
    {
        var diagnostics = RunAnalyzer("""
            using Microsoft.Extensions.DependencyInjection;
            using ModelContextProtocol.Server;
            using System;

            class Test
            {
                void Configure(IMcpServerBuilder builder)
                {
                    builder.WithHttpTransport(options =>
                    {
                        options.IdleTimeout = TimeSpan.FromMinutes(30);
                    });
                }
            }
            """);

        Assert.Single(diagnostics, d => d.Id == "MCP003");
    }

    [Fact]
    public void MethodGroup_Reports()
    {
        var diagnostics = RunAnalyzer("""
            using Microsoft.Extensions.DependencyInjection;
            using ModelContextProtocol.Server;
            using ModelContextProtocol.AspNetCore;

            class Test
            {
                void Configure(IMcpServerBuilder builder)
                {
                    builder.WithHttpTransport(ConfigureTransport);
                }

                static void ConfigureTransport(HttpServerTransportOptions options)
                {
                    options.Stateless = true;
                }
            }
            """);

        // Method groups cannot be traced — analyzer warns.
        Assert.Single(diagnostics, d => d.Id == "MCP003");
    }

    [Fact]
    public void DelegateVariable_Reports()
    {
        var diagnostics = RunAnalyzer("""
            using Microsoft.Extensions.DependencyInjection;
            using ModelContextProtocol.Server;
            using ModelContextProtocol.AspNetCore;
            using System;

            class Test
            {
                void Configure(IMcpServerBuilder builder)
                {
                    Action<HttpServerTransportOptions> configure = o => o.Stateless = true;
                    builder.WithHttpTransport(configure);
                }
            }
            """);

        // Variable references cannot be traced — analyzer warns.
        Assert.Single(diagnostics, d => d.Id == "MCP003");
    }

    [Fact]
    public void NoWithHttpTransportCall_DoesNotReport()
    {
        var diagnostics = RunAnalyzer("""
            class Test
            {
                void DoNothing() { }
            }
            """);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void ParenthesizedLambda_StatelessSet_DoesNotReport()
    {
        var diagnostics = RunAnalyzer("""
            using Microsoft.Extensions.DependencyInjection;
            using ModelContextProtocol.Server;

            class Test
            {
                void Configure(IMcpServerBuilder builder)
                {
                    builder.WithHttpTransport((options) => options.Stateless = false);
                }
            }
            """);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void NamedArgument_NoDelegate_Reports()
    {
        var diagnostics = RunAnalyzer("""
            using Microsoft.Extensions.DependencyInjection;
            using ModelContextProtocol.Server;

            class Test
            {
                void Configure(IMcpServerBuilder builder)
                {
                    builder.WithHttpTransport(configureOptions: null);
                }
            }
            """);

        Assert.Single(diagnostics, d => d.Id == "MCP003");
    }

    [Fact]
    public void NamedArgument_WithStateless_DoesNotReport()
    {
        var diagnostics = RunAnalyzer("""
            using Microsoft.Extensions.DependencyInjection;
            using ModelContextProtocol.Server;

            class Test
            {
                void Configure(IMcpServerBuilder builder)
                {
                    builder.WithHttpTransport(configureOptions: o => o.Stateless = true);
                }
            }
            """);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void UnrelatedWithHttpTransportMethod_DoesNotReport()
    {
        var diagnostics = RunAnalyzer("""
            class SomeBuilder
            {
                public SomeBuilder WithHttpTransport() => this;
            }

            class Test
            {
                void Configure()
                {
                    new SomeBuilder().WithHttpTransport();
                }
            }
            """);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void StaticInvocation_NoDelegate_Reports()
    {
        var diagnostics = RunAnalyzer("""
            using Microsoft.Extensions.DependencyInjection;
            using ModelContextProtocol.Server;

            class Test
            {
                void Configure(IMcpServerBuilder builder)
                {
                    HttpMcpServerBuilderExtensions.WithHttpTransport(builder);
                }
            }
            """);

        Assert.Single(diagnostics, d => d.Id == "MCP003");
    }

    [Fact]
    public void StaticInvocation_WithStateless_DoesNotReport()
    {
        var diagnostics = RunAnalyzer("""
            using Microsoft.Extensions.DependencyInjection;
            using ModelContextProtocol.Server;

            class Test
            {
                void Configure(IMcpServerBuilder builder)
                {
                    HttpMcpServerBuilderExtensions.WithHttpTransport(builder, o => o.Stateless = true);
                }
            }
            """);

        Assert.Empty(diagnostics);
    }

    private List<Diagnostic> RunAnalyzer(string source)
    {
        // Combine stub types with user source.
        var stubTree = CSharpSyntaxTree.ParseText(StubTypes);
        var sourceTree = CSharpSyntaxTree.ParseText(source);

        var references = new List<MetadataReference>
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
        };

        var runtimePath = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        references.Add(MetadataReference.CreateFromFile(Path.Combine(runtimePath, "System.Runtime.dll")));
        references.Add(MetadataReference.CreateFromFile(Path.Combine(runtimePath, "netstandard.dll")));

        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            [stubTree, sourceTree],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var analyzers = ImmutableArray.Create<DiagnosticAnalyzer>(new WithHttpTransportAnalyzer());
        var compilationWithAnalyzers = compilation.WithAnalyzers(analyzers);
        var allDiagnostics = compilationWithAnalyzers.GetAllDiagnosticsAsync().GetAwaiter().GetResult();

        return allDiagnostics.Where(d => d.Id == "MCP003").ToList();
    }
}
