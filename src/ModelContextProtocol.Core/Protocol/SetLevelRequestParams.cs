using System.Text.Json.Serialization;

namespace ModelContextProtocol.Protocol;

/// <summary>
/// Represents the parameters used with a <see cref="RequestMethods.LoggingSetLevel"/> request from a client
/// to enable or adjust logging.
/// </summary>
/// <remarks>
/// This request allows clients to configure the level of logging information they want to receive from the server.
/// The server will send notifications for log events at the specified level and all higher (more severe) levels.
/// </remarks>
[Obsolete(Obsoletions.DeprecatedLogging_Message, DiagnosticId = Obsoletions.Deprecated_DiagnosticId, UrlFormat = Obsoletions.Deprecated_Url)]
public sealed class SetLevelRequestParams : RequestParams
{
    /// <summary>
    /// Gets or sets the level of logging that the client wants to receive from the server. 
    /// </summary>
    [JsonPropertyName("level")]
    public required LoggingLevel Level { get; set; }
}
