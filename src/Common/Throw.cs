using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace ModelContextProtocol;

/// <summary>Provides helper methods for throwing exceptions.</summary>
internal static class Throw
{
    // NOTE: Most of these should be replaced with extension statics for the relevant extension
    // type as downlevel polyfills once the C# 14 extension everything feature is available.

    public static void IfNull([NotNull] object? arg, [CallerArgumentExpression(nameof(arg))] string? parameterName = null)
    {
        if (arg is null)
        {
            ThrowArgumentNullException(parameterName);
        }
    }

    public static void IfNullOrWhiteSpace([NotNull] string? arg, [CallerArgumentExpression(nameof(arg))] string? parameterName = null)
    {
        if (arg is null || arg.AsSpan().IsWhiteSpace())
        {
            ThrowArgumentNullOrWhiteSpaceException(parameterName);
        }
    }

    public static void IfNegative(int arg, [CallerArgumentExpression(nameof(arg))] string? parameterName = null)
    {
        if (arg < 0)
        {
            Throw(parameterName);
            static void Throw(string? parameterName) => throw new ArgumentOutOfRangeException(parameterName, "must not be negative.");
        }
    }

    [DoesNotReturn]
    private static void ThrowArgumentNullOrWhiteSpaceException(string? parameterName)
    {
        if (parameterName is null)
        {
            ThrowArgumentNullException(parameterName);
        }

        throw new ArgumentException("Value cannot be empty or composed entirely of whitespace.", parameterName);
    }

    [DoesNotReturn]
    private static void ThrowArgumentNullException(string? parameterName) => throw new ArgumentNullException(parameterName);
}