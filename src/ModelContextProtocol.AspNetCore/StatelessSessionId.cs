using ModelContextProtocol.Protocol.Types;
using System.Text.Json.Serialization;

namespace ModelContextProtocol.AspNetCore;

internal sealed class StatelessSessionId
{
    [JsonPropertyName("capabilities")]
    public ClientCapabilities? Capabilities { get; init; }

    [JsonPropertyName("clientInfo")]
    public Implementation? ClientInfo { get; init; }

    [JsonPropertyName("userIdClaim")]
    public (string Type, string Value, string Issuer)? UserIdClaim { get; init; }
}
