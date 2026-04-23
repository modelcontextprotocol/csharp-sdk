using System.Diagnostics.CodeAnalysis;

namespace ModelContextProtocol.Protocol;

/// <summary>
/// Represents the capability for listing tasks.
/// </summary>
[Experimental(Experimentals.Tasks_DiagnosticId, UrlFormat = Experimentals.Tasks_Url)]
public sealed class ListMcpTasksCapability;
