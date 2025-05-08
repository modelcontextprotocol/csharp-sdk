namespace ModelContextProtocol.Authentication;

/// <summary>
/// Represents the result of handling an unauthorized response from a resource.
/// </summary>
/// <param name="Success">Indicates if the provider was able to handle the unauthorized response.</param>
/// <param name="RecommendedScheme">The authentication scheme that should be used for the next attempt, if any.</param>
public record McpUnauthorizedResponseResult(bool Success, string? RecommendedScheme);