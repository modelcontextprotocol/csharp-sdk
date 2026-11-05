using ModelContextProtocol.Server;
using System.Diagnostics.CodeAnalysis;

namespace ModelContextProtocol.Extensions.Skills;

/// <summary>Provides a thread-safe collection of <see cref="McpServerSkill"/> instances, indexed by resource-equivalent URIs.</summary>
[Experimental(Experimentals.Skills_DiagnosticId, UrlFormat = Experimentals.Skills_Url)]
public sealed class McpServerSkillCollection()
    : McpServerPrimitiveCollection<McpServerSkill>(SkillUriComparer.Instance);
