using System.Text.Json.Serialization;

namespace ModelContextProtocol.Protocol;

/// <summary>
/// Indicates the intended scope of a cached response, analogous to the HTTP
/// <c>Cache-Control: public</c> and <c>Cache-Control: private</c> directives.
/// </summary>
/// <remarks>
/// <para>
/// This is used by <see cref="ICacheableResult.CacheScope"/> to control who may cache a
/// response returned by <c>tools/list</c>, <c>prompts/list</c>, <c>resources/list</c>,
/// <c>resources/templates/list</c>, and <c>resources/read</c>.
/// </para>
/// <para>
/// When the field is absent from a response, clients should treat it as <see cref="Public"/>.
/// </para>
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<CacheScope>))]
public enum CacheScope
{
    /// <summary>
    /// The response does not contain user-specific data. Any client, shared gateway, or caching
    /// proxy may store and serve the cached response to any user.
    /// </summary>
    /// <remarks>
    /// This is appropriate for lists of tools, prompts, and resource templates that are identical
    /// for all users.
    /// </remarks>
    [JsonStringEnumMemberName("public")]
    Public,

    /// <summary>
    /// The response contains user-specific data. Only the requesting user's client may cache it.
    /// Shared caches (for example, multi-tenant gateways) must not serve the cached response to a
    /// different user.
    /// </summary>
    /// <remarks>
    /// This is appropriate for <c>resources/read</c> results that depend on the authenticated user,
    /// or for filtered list results that vary per user.
    /// </remarks>
    [JsonStringEnumMemberName("private")]
    Private
}
