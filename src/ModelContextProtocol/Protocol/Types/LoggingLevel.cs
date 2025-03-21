using System.Text.Json.Serialization;

namespace ModelContextProtocol.Protocol.Types;

/// <summary>
/// The severity of a log message.
/// These map to syslog message severities, as specified in RFC-5424:
/// https://datatracker.ietf.org/doc/html/rfc5424#section-6.2.1
/// </summary>
public enum LoggingLevel
{
    /// <summary>Detailed debug information, typically only valuable to developers.</summary>
    [JsonPropertyName("debug")]
    Debug,

    /// <summary>Normal operational messages that require no action.</summary>
    [JsonPropertyName("info")]
    Info,

    /// <summary>Normal but significant events that might deserve attention.</summary>
    [JsonPropertyName("notice")]
    Notice,

    /// <summary>Warning conditions that don't represent an error but indicate potential issues.</summary>
    [JsonPropertyName("warning")]
    Warning,

    /// <summary>Error conditions that should be addressed but don't require immediate action.</summary>
    [JsonPropertyName("error")]
    Error,

    /// <summary>Critical conditions that require immediate attention.</summary>
    [JsonPropertyName("critical")]
    Critical,

    /// <summary>Action must be taken immediately to address the condition.</summary>
    [JsonPropertyName("alert")]
    Alert,

    /// <summary>System is unusable and requires immediate attention.</summary>
    [JsonPropertyName("emergency")]
    Emergency
}