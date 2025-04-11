using System.Diagnostics.CodeAnalysis;

namespace ModelContextProtocol.Protocol.Transport;

/// <summary>
/// Provides structural equality comparison for <see cref="SseClientTransportOptions"/>.
/// </summary>
public class SseClientTransportOptionsEqualityComparer : IEqualityComparer<SseClientTransportOptions>
{
    /// <summary>
    /// Gets a default instance of the <see cref="SseClientTransportOptionsEqualityComparer"/>.
    /// </summary>
    public static SseClientTransportOptionsEqualityComparer Default { get; } = new();

    /// <summary>
    /// Determines whether two <see cref="SseClientTransportOptions"/> objects are equal based on their properties.
    /// </summary>
    /// <param name="x">The first object to compare.</param>
    /// <param name="y">The second object to compare.</param>
    /// <returns><see langword="true"/> if the objects are structurally equal; otherwise, <see langword="false"/>.</returns>
    public bool Equals(SseClientTransportOptions? x, SseClientTransportOptions? y)
    {
        if (ReferenceEquals(x, y)) return true;
        if (x is null || y is null) return false;
        
        if (!EqualityComparer<Uri>.Default.Equals(x.Endpoint, y.Endpoint) ||
            !EqualityComparer<string?>.Default.Equals(x.Name, y.Name) ||
            !EqualityComparer<TimeSpan>.Default.Equals(x.ConnectionTimeout, y.ConnectionTimeout) ||
            !EqualityComparer<int>.Default.Equals(x.MaxReconnectAttempts, y.MaxReconnectAttempts) ||
            !EqualityComparer<TimeSpan>.Default.Equals(x.ReconnectDelay, y.ReconnectDelay))
        {
            return false;
        }

        bool headersEqual = (x.AdditionalHeaders is null && y.AdditionalHeaders is null) ||
                            (x.AdditionalHeaders is not null && y.AdditionalHeaders is not null &&
                             x.AdditionalHeaders.Count == y.AdditionalHeaders.Count &&
                             !x.AdditionalHeaders.Except(y.AdditionalHeaders).Any());
        if (!headersEqual)
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// Returns a hash code for the specified <see cref="SseClientTransportOptions"/> object based on its properties.
    /// </summary>
    /// <param name="obj">The object for which a hash code is to be returned.</param>
    /// <returns>A hash code for the specified object.</returns>
    /// <exception cref="ArgumentNullException">The type of <paramref name="obj"/> is a reference type and <paramref name="obj"/> is <see langword="null"/>.</exception>
    public int GetHashCode([DisallowNull] SseClientTransportOptions obj)
    {
        if (obj is null) throw new ArgumentNullException(nameof(obj));
        
        int hashCode = 17; 
        hashCode = hashCode * 23 + (obj.Endpoint?.GetHashCode() ?? 0);
        hashCode = hashCode * 23 + (obj.Name?.GetHashCode() ?? 0);
        hashCode = hashCode * 23 + obj.ConnectionTimeout.GetHashCode();
        hashCode = hashCode * 23 + obj.MaxReconnectAttempts.GetHashCode();
        hashCode = hashCode * 23 + obj.ReconnectDelay.GetHashCode();

        if (obj.AdditionalHeaders is not null)
        {
            foreach (var kvp in obj.AdditionalHeaders.OrderBy(kv => kv.Key))
            {
                hashCode = hashCode * 23 + kvp.Key.GetHashCode();
                hashCode = hashCode * 23 + (kvp.Value?.GetHashCode() ?? 0);
            }
        }
        return hashCode;
    }
} 