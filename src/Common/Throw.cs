using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace ModelContextProtocol.Utils;

/// <summary>Provides helper methods for throwing exceptions.</summary>
internal static class Throw
{
    // NOTE: Most of these should be replaced with extension statics for the relevant extension
    // type as downlevel polyfills once the C# 14 extension everything feature is available.

    /// <summary>
    /// Throws an <see cref="ArgumentNullException"/> if the specified argument is null.
    /// </summary>
    /// <param name="arg">The argument to check for null.</param>
    /// <param name="parameterName">The name of the parameter being checked. This value is automatically provided by the compiler.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="arg"/> is null.</exception>
    /// <example>
    /// <code>
    /// public void ProcessData(DataObject data)
    /// {
    ///     Throw.IfNull(data); // Throws ArgumentNullException if data is null
    ///     
    ///     // Process the data
    /// }
    /// </code>
    /// </example>
    public static void IfNull([NotNull] object? arg, [CallerArgumentExpression(nameof(arg))] string? parameterName = null)
    {
        if (arg is null)
        {
            ThrowArgumentNullException(parameterName);
        }
    }

    /// <summary>
    /// Throws an <see cref="ArgumentException"/> if the specified string is null, empty, or consists only of white-space characters.
    /// </summary>
    /// <param name="arg">The string argument to check.</param>
    /// <param name="parameterName">The name of the parameter being checked. This value is automatically provided by the compiler.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="arg"/> is null.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="arg"/> is empty or consists only of white-space characters.</exception>
    /// <example>
    /// <code>
    /// public void ProcessMessage(string messageId)
    /// {
    ///     Throw.IfNullOrWhiteSpace(messageId); // Throws if messageId is null, empty, or whitespace
    ///     
    ///     // Process the message
    /// }
    /// </code>
    /// </example>
    public static void IfNullOrWhiteSpace([NotNull] string? arg, [CallerArgumentExpression(nameof(arg))] string? parameterName = null)
    {
        if (arg is null || arg.AsSpan().IsWhiteSpace())
        {
            ThrowArgumentNullOrWhiteSpaceException(parameterName);
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
