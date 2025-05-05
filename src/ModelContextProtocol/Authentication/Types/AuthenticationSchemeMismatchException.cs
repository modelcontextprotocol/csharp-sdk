namespace ModelContextProtocol.Authentication.Types;

/// <summary>
/// Exception thrown when no compatible authentication scheme can be found between the client and server.
/// </summary>
public class AuthenticationSchemeMismatchException : Exception
{
    /// <summary>
    /// Gets the authentication schemes supported by the server.
    /// </summary>
    public IReadOnlyList<string> ServerSchemes { get; }

    /// <summary>
    /// Gets the authentication schemes supported by the client provider.
    /// </summary>
    public IReadOnlyList<string> ProviderSchemes { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="AuthenticationSchemeMismatchException"/> class.
    /// </summary>
    /// <param name="message">The exception message.</param>
    /// <param name="serverSchemes">The authentication schemes supported by the server.</param>
    /// <param name="providerSchemes">The authentication schemes supported by the client provider.</param>
    public AuthenticationSchemeMismatchException(
        string message,
        IEnumerable<string> serverSchemes,
        IEnumerable<string> providerSchemes)
        : base(message)
    {
        ServerSchemes = serverSchemes.ToList().AsReadOnly();
        ProviderSchemes = providerSchemes.ToList().AsReadOnly();
    }
}
