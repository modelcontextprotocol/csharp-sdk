using ModelContextProtocol.Server;
using System.Diagnostics.CodeAnalysis;

namespace ModelContextProtocol.Extensions.Skills;

/// <summary>Represents a registered skill with an optional listing entry and a get operation.</summary>
/// <remarks>
/// File authoring is provided by <see cref="McpServerSkillPackage"/>. Register skills and file resources
/// independently, or register both components with <see cref="McpSkillsBuilderExtensions.WithSkillPackages"/>.
/// </remarks>
[Experimental(Experimentals.Skills_DiagnosticId, UrlFormat = Experimentals.Skills_Url)]
public abstract class McpServerSkill : IMcpServerPrimitive
{
    /// <summary>Initializes a registration with a stable skill URI.</summary>
    /// <param name="uri">The URI of the skill's <c>SKILL.md</c>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="uri"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="uri"/> is not a valid skill URI.</exception>
    protected McpServerSkill(string uri)
    {
        if (uri is null) throw new ArgumentNullException(nameof(uri));
        SkillValidation.GetSkillRoot(uri, nameof(uri));
        SkillValidation.ValidateUriShape(uri, "The skill URI", nameof(uri));
        Id = uri;
    }

    /// <summary>Gets the skill's immutable identity: the exact URI text of its <c>SKILL.md</c>.</summary>
    public string Id { get; }

    /// <summary>Gets the server-local metadata associated with this primitive.</summary>
    /// <remarks>
    /// Metadata is not serialized, copied to file resources, or interpreted as authorization. Applications can
    /// inspect it on the collection and through the matched primitive in <see cref="McpSkillsOptions.GetSkillFilters"/>.
    /// </remarks>
    public virtual IReadOnlyList<object> Metadata => [];

    /// <summary>Gets the complete entry contributed to listings, or null to omit this registration.</summary>
    /// <remarks>
    /// Listing reads this property without invoking <see cref="GetAsync"/>. A null value does not prevent direct
    /// retrieval. Use <see cref="McpSkillsOptions.ListSkillsHandler"/> for request-dependent listings.
    /// </remarks>
    public virtual Skill? ProtocolSkill => null;

    /// <summary>Retrieves the skill's complete entry for the current get request.</summary>
    /// <param name="request">The actual get context, including matched primitive, caller, services, and metadata.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation.</param>
    /// <returns>The complete result, including optional metadata and cache hints.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is null.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> is canceled.</exception>
    /// <exception cref="InvalidOperationException">The request URI does not match this registration.</exception>
    /// <remarks>
    /// The SDK invokes this method only for matched get requests, after get filters. It never invokes it during
    /// listing or initialization. The final result is validated and bound to the original requested URI,
    /// and shaped for the negotiated protocol after all filters.
    /// </remarks>
    public abstract ValueTask<GetSkillResult> GetAsync(
        RequestContext<GetSkillRequestParams> request, CancellationToken cancellationToken = default);

    /// <inheritdoc />
    public override string ToString() => Id;

    /// <summary>Creates a fixed primitive by validating an existing entry.</summary>
    /// <param name="protocolSkill">The protocol entry.</param>
    /// <param name="metadata">Optional server-local metadata.</param>
    /// <returns>The fixed skill primitive.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="protocolSkill"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="protocolSkill"/> is invalid.</exception>
    /// <remarks>
    /// Keep the entry's URI consistent with the immutable registration identity.
    /// No file resources are created; the application serves them separately.
    /// </remarks>
    public static McpServerSkill Create(Skill protocolSkill, IReadOnlyList<object>? metadata = null) =>
        Create(protocolSkill, metadata, nameof(protocolSkill));

    internal static McpServerSkill Create(
        Skill protocolSkill, IReadOnlyList<object>? metadata, string paramName, string? uriParamName = null)
    {
        SkillValidation.Validate(protocolSkill, paramName, uriParamName);
        return new FixedMcpServerSkill(protocolSkill, metadata);
    }

    private sealed class FixedMcpServerSkill : McpServerSkill
    {
        private readonly Skill _entry;

        public FixedMcpServerSkill(Skill entry, IReadOnlyList<object>? metadata) : base(entry.Uri)
        {
            _entry = entry;
            Metadata = metadata ?? [];
        }

        public override Skill ProtocolSkill => _entry;
        public override IReadOnlyList<object> Metadata { get; }

        public override ValueTask<GetSkillResult> GetAsync(
            RequestContext<GetSkillRequestParams> request, CancellationToken cancellationToken = default)
        {
            if (request is null) throw new ArgumentNullException(nameof(request));
            cancellationToken.ThrowIfCancellationRequested();
            if (!SkillUriComparer.Instance.Equals(Id, request.Params?.Uri))
            {
                throw new InvalidOperationException($"Skill '{Id}' does not match the requested URI '{request.Params?.Uri}'.");
            }

            return new(new GetSkillResult { Skill = ProtocolSkill });
        }
    }
}
