using System.Diagnostics.CodeAnalysis;

namespace ModelContextProtocol.Protocol.Transport;

/// <summary>
/// Provides structural equality comparison for <see cref="StdioClientTransportOptions"/>.
/// </summary>
public class StdioClientTransportOptionsEqualityComparer : IEqualityComparer<StdioClientTransportOptions>
{
    /// <summary>
    /// Gets a default instance of the <see cref="StdioClientTransportOptionsEqualityComparer"/>.
    /// </summary>
    public static StdioClientTransportOptionsEqualityComparer Default { get; } = new();

    /// <summary>
    /// Determines whether two <see cref="StdioClientTransportOptions"/> objects are equal based on their properties.
    /// </summary>
    /// <param name="x">The first object to compare.</param>
    /// <param name="y">The second object to compare.</param>
    /// <returns><see langword="true"/> if the objects are structurally equal; otherwise, <see langword="false"/>.</returns>
    public bool Equals(StdioClientTransportOptions? x, StdioClientTransportOptions? y)
    {
        if (ReferenceEquals(x, y)) return true;
        if (x is null || y is null) return false;
        
        if (!EqualityComparer<string>.Default.Equals(x.Command, y.Command) ||
            !EqualityComparer<string?>.Default.Equals(x.Name, y.Name) ||
            !EqualityComparer<string?>.Default.Equals(x.WorkingDirectory, y.WorkingDirectory) ||
            !EqualityComparer<TimeSpan>.Default.Equals(x.ShutdownTimeout, y.ShutdownTimeout))
        {
            return false;
        }
        
        bool argumentsEqual = (x.Arguments is null && y.Arguments is null) ||
                              (x.Arguments is not null && y.Arguments is not null && x.Arguments.SequenceEqual(y.Arguments));
        if (!argumentsEqual)
        {
            return false;
        }
        
        bool envVarsEqual = (x.EnvironmentVariables is null && y.EnvironmentVariables is null) ||
                            (x.EnvironmentVariables is not null && y.EnvironmentVariables is not null &&
                             x.EnvironmentVariables.Count == y.EnvironmentVariables.Count &&
                             !x.EnvironmentVariables.Except(y.EnvironmentVariables).Any());
        if (!envVarsEqual)
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// Returns a hash code for the specified <see cref="StdioClientTransportOptions"/> object based on its properties.
    /// </summary>
    /// <param name="obj">The object for which a hash code is to be returned.</param>
    /// <returns>A hash code for the specified object.</returns>
    /// <exception cref="ArgumentNullException">The type of <paramref name="obj"/> is a reference type and <paramref name="obj"/> is <see langword="null"/>.</exception>
    public int GetHashCode([DisallowNull] StdioClientTransportOptions obj)
    {
        if (obj is null) throw new ArgumentNullException(nameof(obj));
        
        int hashCode = 17; 
        hashCode = hashCode * 23 + (obj.Command?.GetHashCode() ?? 0);
        hashCode = hashCode * 23 + (obj.Name?.GetHashCode() ?? 0);
        hashCode = hashCode * 23 + (obj.WorkingDirectory?.GetHashCode() ?? 0);
        hashCode = hashCode * 23 + obj.ShutdownTimeout.GetHashCode();

        if (obj.Arguments is not null)
        {
            foreach (var arg in obj.Arguments)
            {
                hashCode = hashCode * 23 + (arg?.GetHashCode() ?? 0);
            }
        }

        if (obj.EnvironmentVariables is not null)
        {
            // Order by key for consistency
            foreach (var kvp in obj.EnvironmentVariables.OrderBy(kv => kv.Key))
            {
                hashCode = hashCode * 23 + kvp.Key.GetHashCode();
                hashCode = hashCode * 23 + (kvp.Value?.GetHashCode() ?? 0);
            }
        }
        return hashCode;
    }
} 