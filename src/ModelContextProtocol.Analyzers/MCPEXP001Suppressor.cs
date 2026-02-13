using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using System.Collections.Immutable;

namespace ModelContextProtocol.Analyzers;

/// <summary>
/// Suppresses MCPEXP001 diagnostics in source-generated code.
/// </summary>
/// <remarks>
/// <para>
/// The MCP SDK uses <c>object?</c> backing fields with <c>[JsonConverter(typeof(ExperimentalJsonConverter&lt;T&gt;))]</c>
/// to handle serialization of experimental types. When consumers define their own <c>JsonSerializerContext</c>,
/// the System.Text.Json source generator emits code referencing these converters with experimental type arguments,
/// which triggers MCPEXP001 diagnostics in the generated code.
/// </para>
/// <para>
/// This suppressor suppresses MCPEXP001 only in source-generated files (identified by <c>.g.cs</c> file extension),
/// so that hand-written user code that directly references experimental types still produces the diagnostic.
/// </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class MCPEXP001Suppressor : DiagnosticSuppressor
{
    private static readonly SuppressionDescriptor SuppressInGeneratedCode = new(
        id: "MCP_MCPEXP001_GENERATED",
        suppressedDiagnosticId: "MCPEXP001",
        justification: "MCPEXP001 is suppressed in source-generated code because the experimental type reference originates from the MCP SDK's backing field infrastructure, not from user code.");

    /// <inheritdoc/>
    public override ImmutableArray<SuppressionDescriptor> SupportedSuppressions =>
        ImmutableArray.Create(SuppressInGeneratedCode);

    /// <inheritdoc/>
    public override void ReportSuppressions(SuppressionAnalysisContext context)
    {
        foreach (Diagnostic diagnostic in context.ReportedDiagnostics)
        {
            if (diagnostic.Id == "MCPEXP001" && IsInGeneratedCode(diagnostic))
            {
                context.ReportSuppression(Suppression.Create(SuppressInGeneratedCode, diagnostic));
            }
        }
    }

    private static bool IsInGeneratedCode(Diagnostic diagnostic)
    {
        string? filePath = diagnostic.Location.SourceTree?.FilePath;
        return filePath is not null && filePath.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase);
    }
}
