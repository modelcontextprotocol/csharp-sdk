using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using System.Collections.Immutable;
using System.Threading;

namespace ModelContextProtocol.Analyzers;

/// <summary>
/// Reports a warning when <c>WithHttpTransport</c> is called without explicitly setting
/// <c>HttpServerTransportOptions.Stateless</c> to <see langword="true"/> or <see langword="false"/>.
/// </summary>
/// <remarks>
/// <para>
/// The default value of <c>Stateless</c> is <see langword="false"/> (stateful mode), but stateless mode is
/// recommended for most servers. Setting the property explicitly protects against future changes to the default.
/// </para>
/// <para>
/// The analyzer detects the following patterns:
/// <list type="bullet">
/// <item><c>.WithHttpTransport()</c> — no delegate passed.</item>
/// <item><c>.WithHttpTransport(null)</c> — null literal passed.</item>
/// <item><c>.WithHttpTransport(o =&gt; ...)</c> — lambda that does not assign to <c>o.Stateless</c>.</item>
/// <item><c>.WithHttpTransport(ConfigureMethod)</c> — method group (cannot trace into the body).</item>
/// </list>
/// </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class WithHttpTransportAnalyzer : DiagnosticAnalyzer
{
    private const string WithHttpTransportMethodName = "WithHttpTransport";
    private const string StatelessPropertyName = "Stateless";

    /// <inheritdoc/>
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Diagnostics.WithHttpTransportStatelessNotSet);

    /// <inheritdoc/>
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterCompilationStartAction(compilationContext =>
        {
            INamedTypeSymbol? extensionsType = compilationContext.Compilation.GetTypeByMetadataName(
                McpAttributeNames.HttpMcpServerBuilderExtensions);

            if (extensionsType is null)
            {
                // The AspNetCore package isn't referenced; nothing to analyze.
                return;
            }

            INamedTypeSymbol? optionsType = compilationContext.Compilation.GetTypeByMetadataName(
                McpAttributeNames.HttpServerTransportOptions);

            if (optionsType is null)
            {
                return;
            }

            compilationContext.RegisterSyntaxNodeAction(
                nodeContext => AnalyzeInvocation(nodeContext, extensionsType, optionsType),
                SyntaxKind.InvocationExpression);
        });
    }

    private static void AnalyzeInvocation(
        SyntaxNodeAnalysisContext context,
        INamedTypeSymbol extensionsType,
        INamedTypeSymbol optionsType)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;

        // Quick syntactic check: does the method name end with "WithHttpTransport"?
        string? methodName = GetMethodName(invocation);
        if (methodName != WithHttpTransportMethodName)
        {
            return;
        }

        // Resolve the method symbol to confirm it's the right one.
        if (context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken).Symbol is not IMethodSymbol methodSymbol)
        {
            return;
        }

        // Check that the method belongs to HttpMcpServerBuilderExtensions (comparing the original definition for generic cases).
        IMethodSymbol originalMethod = methodSymbol.ReducedFrom ?? methodSymbol;
        if (!SymbolEqualityComparer.Default.Equals(originalMethod.ContainingType, extensionsType))
        {
            return;
        }

        // Determine the configureOptions argument.
        bool isExtensionInvocation = methodSymbol.ReducedFrom is not null;
        ExpressionSyntax? configureArg = GetConfigureOptionsArgument(invocation, originalMethod, isExtensionInvocation);

        if (configureArg is null || IsNullLiteral(configureArg))
        {
            // No delegate or explicit null — warn.
            context.ReportDiagnostic(Diagnostic.Create(
                Diagnostics.WithHttpTransportStatelessNotSet,
                invocation.GetLocation()));
            return;
        }

        // If the argument is a lambda or anonymous method, check whether it assigns to Stateless.
        if (configureArg is AnonymousFunctionExpressionSyntax lambda)
        {
            if (!LambdaAssignsStateless(lambda, context.SemanticModel, optionsType, context.CancellationToken))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    Diagnostics.WithHttpTransportStatelessNotSet,
                    invocation.GetLocation()));
            }
            return;
        }

        // For method groups, delegate variables, or other expressions we cannot analyze easily — warn.
        context.ReportDiagnostic(Diagnostic.Create(
            Diagnostics.WithHttpTransportStatelessNotSet,
            invocation.GetLocation()));
    }

    private static string? GetMethodName(InvocationExpressionSyntax invocation)
    {
        return invocation.Expression switch
        {
            MemberAccessExpressionSyntax memberAccess => memberAccess.Name.Identifier.Text,
            IdentifierNameSyntax identifier => identifier.Identifier.Text,
            _ => null,
        };
    }

    /// <summary>
    /// Extracts the <c>configureOptions</c> argument from the invocation, handling both positional and named arguments.
    /// Returns <see langword="null"/> if no argument was provided (relying on the default <c>null</c>).
    /// </summary>
    private static ExpressionSyntax? GetConfigureOptionsArgument(
        InvocationExpressionSyntax invocation,
        IMethodSymbol originalMethod,
        bool isExtensionInvocation)
    {
        ArgumentListSyntax argumentList = invocation.ArgumentList;

        // If called as an extension method via member access (builder.WithHttpTransport(...)),
        // the configureOptions parameter is at index 1 in the original definition but index 0 in the call.
        // If called as a static method (HttpMcpServerBuilderExtensions.WithHttpTransport(builder, ...)),
        // it's at index 1 in the call.
        // We match by parameter name for robustness.

        foreach (ArgumentSyntax argument in argumentList.Arguments)
        {
            if (argument.NameColon is not null)
            {
                if (argument.NameColon.Name.Identifier.Text == "configureOptions")
                {
                    return argument.Expression;
                }
            }
        }

        // No named argument found — try positional.
        // Find the parameter index of configureOptions in the original method.
        int paramIndex = -1;
        for (int i = 0; i < originalMethod.Parameters.Length; i++)
        {
            if (originalMethod.Parameters[i].Name == "configureOptions")
            {
                paramIndex = i;
                break;
            }
        }

        if (paramIndex < 0)
        {
            return null;
        }

        // Adjust index: if called as an extension method (builder.WithHttpTransport(...)),
        // the 'this' parameter is implicit in the call args.
        int callArgIndex = isExtensionInvocation ? paramIndex - 1 : paramIndex;

        if (callArgIndex >= 0 && callArgIndex < argumentList.Arguments.Count)
        {
            return argumentList.Arguments[callArgIndex].Expression;
        }

        return null;
    }

    private static bool IsNullLiteral(ExpressionSyntax expression)
    {
        // Handle both 'null' and 'default' / 'default(Action<HttpServerTransportOptions>)'.
        return expression.IsKind(SyntaxKind.NullLiteralExpression) ||
               expression.IsKind(SyntaxKind.DefaultLiteralExpression) ||
               expression.IsKind(SyntaxKind.DefaultExpression);
    }

    /// <summary>
    /// Checks whether a lambda or anonymous method assigns to the <c>Stateless</c> property
    /// on its parameter (which should be of type <c>HttpServerTransportOptions</c>).
    /// </summary>
    private static bool LambdaAssignsStateless(
        AnonymousFunctionExpressionSyntax lambda,
        SemanticModel semanticModel,
        INamedTypeSymbol optionsType,
        CancellationToken cancellationToken)
    {
        // Get the parameter name used by the lambda. For simple lambdas (o => ...),
        // ParenthesizedLambda (Action<T> can have (options) => ...), or anonymous delegates.
        string? parameterName = GetLambdaParameterName(lambda);

        // Walk all descendant assignment expressions looking for assignments to <param>.Stateless.
        foreach (AssignmentExpressionSyntax assignment in lambda.DescendantNodes().OfType<AssignmentExpressionSyntax>())
        {
            if (IsStatelessAssignment(assignment, parameterName, semanticModel, optionsType, cancellationToken))
            {
                return true;
            }
        }

        return false;
    }

    private static string? GetLambdaParameterName(AnonymousFunctionExpressionSyntax lambda)
    {
        if (lambda is SimpleLambdaExpressionSyntax simpleLambda)
        {
            return simpleLambda.Parameter.Identifier.Text;
        }

        if (lambda is ParenthesizedLambdaExpressionSyntax parenthesizedLambda &&
            parenthesizedLambda.ParameterList.Parameters.Count > 0)
        {
            return parenthesizedLambda.ParameterList.Parameters[0].Identifier.Text;
        }

        if (lambda is AnonymousMethodExpressionSyntax anonymousMethod &&
            anonymousMethod.ParameterList is not null &&
            anonymousMethod.ParameterList.Parameters.Count > 0)
        {
            return anonymousMethod.ParameterList.Parameters[0].Identifier.Text;
        }

        return null;
    }

    /// <summary>
    /// Determines whether an assignment expression targets the <c>Stateless</c> property
    /// on <c>HttpServerTransportOptions</c>. Uses a fast syntactic check first, then confirms
    /// via the semantic model.
    /// </summary>
    private static bool IsStatelessAssignment(
        AssignmentExpressionSyntax assignment,
        string? parameterName,
        SemanticModel semanticModel,
        INamedTypeSymbol optionsType,
        CancellationToken cancellationToken)
    {
        // Fast syntactic check: is the left side <something>.Stateless?
        if (assignment.Left is not MemberAccessExpressionSyntax memberAccess ||
            memberAccess.Name.Identifier.Text != StatelessPropertyName)
        {
            return false;
        }

        // If we know the parameter name, check that the receiver matches.
        if (parameterName is not null &&
            memberAccess.Expression is IdentifierNameSyntax identifier &&
            identifier.Identifier.Text != parameterName)
        {
            return false;
        }

        // Confirm via semantic model that the property belongs to HttpServerTransportOptions.
        SymbolInfo symbolInfo = semanticModel.GetSymbolInfo(memberAccess, cancellationToken);
        if (symbolInfo.Symbol is IPropertySymbol propertySymbol &&
            SymbolEqualityComparer.Default.Equals(propertySymbol.ContainingType, optionsType))
        {
            return true;
        }

        return false;
    }
}
