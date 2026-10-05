using ModelContextProtocol.Protocol;
using System.Text.Json.Serialization;

namespace ModelContextProtocol.Extensions.Skills;

/// <summary>
/// Represents the parameters for a <c>skills/list</c> request enumerating the skills a server serves.
/// </summary>
/// <remarks>
/// <para>
/// See the <see href="https://github.com/modelcontextprotocol/modelcontextprotocol/pull/2640">SEP-2640</see>
/// specification for details.
/// </para>
/// </remarks>
public sealed class ListSkillsRequestParams : PaginatedRequestParams;
