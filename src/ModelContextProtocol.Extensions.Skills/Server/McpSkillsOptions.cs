using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.Diagnostics.CodeAnalysis;

namespace ModelContextProtocol.Extensions.Skills;

/// <summary>
/// Configures the behavior of the MCP Skills extension on a server.
/// </summary>
[Experimental(Experimentals.Skills_DiagnosticId, UrlFormat = Experimentals.Skills_Url)]
public sealed class McpSkillsOptions
{
    /// <summary>
    /// Gets or sets the registered server-side skills.
    /// </summary>
    /// <remarks>
    /// DI contributions use <c>TryAdd</c> during configuration, before the callback supplied to <c>WithSkills</c>.
    /// Later configuration can inspect, remove, or replace them; the first accepted identity wins when adding.
    /// Non-null listing manifests are checked once during server initialization, not on
    /// runtime mutations. File resources are registered independently through <c>WithResources</c> or
    /// <see cref="McpSkillsBuilderExtensions.WithSkillPackages"/>, even when configuration removes a skill.
    /// Collection changes affect Skills listing/get only. Manage resource availability separately;
    /// removing an entry does not revoke access to its resources.
    /// </remarks>
    public McpServerSkillCollection SkillCollection { get; set; } = new();

    /// <summary>
    /// Gets or sets a handler for listing application-supplied skills.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The handler owns its page and cursor. Non-null registered <see cref="McpServerSkill.ProtocolSkill"/> entries
    /// are appended only when the request cursor is null, without invoking <see cref="McpServerSkill.GetAsync"/>.
    /// The handler may list get-only registrations, but must not duplicate non-null registered listing entries.
    /// Omitting a skill from application listings does not prevent direct retrieval.
    /// </para>
    /// <para>
    /// Resolve application dependencies from the supplied context's <see cref="MessageContext.Services"/>.
    /// The SDK owns serialization, entry validation, and protocol-version gating of cache hints. A handler's
    /// cache hints apply to the complete augmented page, including registered entries. An otherwise empty result
    /// can supply hints for a static listing. Public caching must be valid for every returned entry. When hints
    /// are omitted, conservative wire defaults apply. Enforce per-caller visibility; the built-in resource
    /// authorization filters do not run for Skills requests. <see cref="ListSkillsFilters"/> wrap the complete
    /// listing, including registered entries.
    /// Composition appends to the returned list directly, as Core does. Return a fresh mutable page when
    /// registrations are included.
    /// </para>
    /// </remarks>
    public McpRequestHandler<ListSkillsRequestParams, ListSkillsResult>? ListSkillsHandler { get; set; }

    /// <summary>
    /// Gets or sets the fallback handler for skills not found in <see cref="SkillCollection"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Matched registrations invoke <see cref="McpServerSkill.GetAsync"/> before this fallback, even when their
    /// <see cref="McpServerSkill.ProtocolSkill"/> is null. This handler cannot intercept or authorize registrations;
    /// use <see cref="GetSkillFilters"/> to wrap both dispatch paths. Registered implementations and filters may
    /// supply per-result cache hints; omitted hints default to zero/private on modern protocols. Older protocols omit them.
    /// Throw <see cref="McpProtocolException"/> with
    /// <see cref="McpErrorCode.InvalidParams"/> when the caller cannot retrieve the requested skill.
    /// </para>
    /// <para>
    /// Authorize application-owned entries before pagination and protect the corresponding file resources
    /// separately. Local metadata does not grant authorization automatically. Skills participation in resource
    /// authorization filters is not implemented.
    /// Services, serialization, validation, caching, and authorization follow <see cref="ListSkillsHandler"/>.
    /// </para>
    /// </remarks>
    public McpRequestHandler<GetSkillRequestParams, GetSkillResult>? GetSkillHandler { get; set; }

    /// <summary>Gets or sets the filters for the complete Skills listing pipeline.</summary>
    /// <remarks>
    /// Filters run in registration order and wrap the custom handler and registered-entry augmentation.
    /// They can inspect the caller, short-circuit the request, and modify the complete page and its cache hints.
    /// Authorize paginated application sources before taking a page; filtering a finished page does not
    /// make its cursor safe. Results returned by filters are validated before serialization.
    /// Configure filters before server initialization, and resolve scoped dependencies inside the returned
    /// handler using <see cref="MessageContext.Services"/>.
    /// </remarks>
    public IList<McpRequestFilter<ListSkillsRequestParams, ListSkillsResult>> ListSkillsFilters
    {
        get => field ??= [];
        set => field = value ?? throw new ArgumentNullException(nameof(value));
    }

    /// <summary>Gets or sets the filters for the complete Skills get pipeline.</summary>
    /// <remarks>
    /// Filters run in registration order after URI matching, before either the registered get or custom fallback.
    /// <see cref="RequestContext{TParams}.MatchedPrimitive"/> contains the matched <see cref="McpServerSkill"/>,
    /// or is null for an unregistered URI. Filters can authorize the request, short-circuit dispatch, and modify
    /// the result and cache hints. The final entry is validated and bound to the original request URI.
    /// Configure filters before server initialization, and resolve scoped dependencies inside the returned
    /// handler using <see cref="MessageContext.Services"/>. These filters do not protect file-resource requests.
    /// </remarks>
    public IList<McpRequestFilter<GetSkillRequestParams, GetSkillResult>> GetSkillFilters
    {
        get => field ??= [];
        set => field = value ?? throw new ArgumentNullException(nameof(value));
    }
}
