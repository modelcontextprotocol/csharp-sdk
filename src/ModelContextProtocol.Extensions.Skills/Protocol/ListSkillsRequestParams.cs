using ModelContextProtocol.Protocol;

namespace ModelContextProtocol.Extensions.Skills;

/// <summary>
/// Represents the parameters for a <c>skills/list</c> request enumerating the skills a server serves.
/// </summary>
/// <remarks>
/// See the <see href="https://github.com/modelcontextprotocol/ext-skills/blob/main/specification/stable/skills.mdx">Skills extension specification</see>
/// for details.
/// </remarks>
public sealed class ListSkillsRequestParams : PaginatedRequestParams;
