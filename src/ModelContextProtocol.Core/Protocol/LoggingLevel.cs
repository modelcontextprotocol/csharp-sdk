using System.Text.Json.Serialization;

namespace ModelContextProtocol.Protocol;

/// <summary>
/// Indicates the severity of a log message.
/// </summary>
/// <remarks>
/// These values map to syslog message severities, as specified in <see href="https://datatracker.ietf.org/doc/html/rfc5424#section-6.2.1">RFC-5424</see>.
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<LoggingLevel>))]
public enum LoggingLevel
{
    /// <summary>Detailed debug information, typically only valuable to developers.</summary>
    [JsonStringEnumMemberName("debug")]
    Debug,

    /// <summary>Normal operational messages that require no action.</summary>
    [JsonStringEnumMemberName("info")]
    Info,

    /// <summary>Normal but significant events that might deserve attention.</summary>
    [JsonStringEnumMemberName("notice")]
    Notice,

    /// <summary>Warning conditions that don't represent an error but indicate potential issues.</summary>
    [JsonStringEnumMemberName("warning")]
    Warning,

    /// <summary>Error conditions that should be addressed but don't require immediate action.</summary>
    [JsonStringEnumMemberName("error")]
    Error,

    /// <summary>Critical conditions that require immediate attention.</summary>
    [JsonStringEnumMemberName("critical")]
    Critical,

    /// <summary>Action must be taken immediately to address the condition.</summary>
    [JsonStringEnumMemberName("alert")]
    Alert,

    /// <summary>System is unusable and requires immediate attention.</summary>
    [JsonStringEnumMemberName("emergency")]
    Emergency
}
