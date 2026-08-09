using System.Diagnostics.CodeAnalysis;

namespace ModelContextProtocol;

/// <summary>
/// Provides the single, shared entry point used by exception-logging callsites to apply a
/// user-supplied exception summarizer.
/// </summary>
internal static class ExceptionSummaryHelper
{
    /// <summary>
    /// Attempts to produce a sanitized description of <paramref name="exception"/> using <paramref name="summarizer"/>.
    /// Callers check that a summarizer is configured, and that the event's level is enabled, before calling this.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> if a summary was produced and the caller should log it in place of
    /// <paramref name="exception"/>; otherwise, <see langword="false"/>, in which case the caller must log
    /// <paramref name="exception"/> exactly as it would have without a summarizer. The summarizer is supplied
    /// by the host, so throwing or returning <see langword="null"/> both fall back rather than disrupt the session.
    /// </returns>
    public static bool TrySummarize(Func<Exception, string> summarizer, Exception exception, [NotNullWhen(true)] out string? summary)
    {
        try
        {
            summary = summarizer(exception);
            return summary is not null;
        }
        catch
        {
            // A faulty summarizer must never fail logging; fall back to the raw exception.
            summary = null;
            return false;
        }
    }
}
