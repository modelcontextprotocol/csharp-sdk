using ModelContextProtocol.Protocol;
using System.Diagnostics.CodeAnalysis;

namespace ModelContextProtocol.Extensions.Skills;

/// <summary>
/// Represents the parameters for a <c>skills/list</c> request enumerating the skills a server serves.
/// </summary>
/// <remarks>
/// See the <see href="https://github.com/modelcontextprotocol/ext-skills/blob/main/specification/beta/skills.mdx">Skills extension specification</see>
/// for details.
/// </remarks>
[Experimental(Experimentals.Skills_DiagnosticId, UrlFormat = Experimentals.Skills_Url)]
public sealed class ListSkillsRequestParams : PaginatedRequestParams;
