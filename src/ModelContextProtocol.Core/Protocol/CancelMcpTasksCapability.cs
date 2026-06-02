using System.Diagnostics.CodeAnalysis;

namespace ModelContextProtocol.Protocol;

/// <summary>
/// Represents the capability for cancelling tasks.
/// </summary>
[Experimental(Experimentals.Tasks_DiagnosticId, UrlFormat = Experimentals.Tasks_Url)]
public sealed class CancelMcpTasksCapability;
