using ModelContextProtocol.Protocol.Types;
using System.Text.Json.Serialization;

namespace ModelContextProtocol.AspNetCore;

internal sealed class StatelessSessionId
{
    [JsonPropertyName("clientInfo")]
    public Implementation? ClientInfo { get; init; }

    [JsonPropertyName("userIdClaim")]
    public StatelessUserId? UserIdClaim { get; init; }
}
