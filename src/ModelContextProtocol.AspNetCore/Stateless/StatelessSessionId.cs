using ModelContextProtocol.Protocol.Types;
using System.Text.Json.Serialization;

namespace ModelContextProtocol.AspNetCore.Stateless;

internal sealed class StatelessSessionId
{
    [JsonPropertyName("clientInfo")]
    public Implementation? ClientInfo { get; init; }

    [JsonPropertyName("userIdClaim")]
    public UserIdClaim? UserIdClaim { get; init; }
}
