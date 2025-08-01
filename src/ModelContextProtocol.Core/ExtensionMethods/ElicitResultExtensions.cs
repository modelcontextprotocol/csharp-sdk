using ModelContextProtocol.Protocol;

namespace ModelContextProtocol.ExtensionMethods;

/// <summary>
/// Provides extension methods for interacting with an <see cref="ElicitResult"/> instance.
/// </summary>
public static class ElicitResultExtensions
{
    /// <summary>
    /// Determines whether given <see cref="ElicitResult"/> represents an accepted action.
    /// </summary>
    /// <param name="result">Elicit result to check.</param>
    /// <returns><see langword="true"/> if the action is "accept"; otherwise, <see langword="false"/>.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="result"/> is <see langword="null"/>.</exception>
    public static bool IsAccepted(this ElicitResult result)
    {
        Throw.IfNull(result);
        return string.Equals(result.Action, "accept", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Determines whether given <see cref="ElicitResult"/> represents a declined action.
    /// </summary>
    /// <param name="result">Elicit result to check.</param>
    /// <returns><see langword="true"/> if the action is "decline"; otherwise, <see langword="false"/>.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="result"/> is <see langword="null"/>.</exception>
    public static bool IsDeclined(this ElicitResult result)
    {
        Throw.IfNull(result);
        return string.Equals(result.Action, "decline", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Determines whether given <see cref="ElicitResult"/> represents a cancelled action.
    /// </summary>
    /// <param name="result">Elicit result to check.</param>
    /// <returns><see langword="true"/> if the action is "cancel"; otherwise, <see langword="false"/>.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="result"/> is <see langword="null"/>.</exception>
    public static bool IsCancelled(this ElicitResult result)
    {
        Throw.IfNull(result);
        return string.Equals(result.Action, "cancel", StringComparison.OrdinalIgnoreCase);
    }
}
