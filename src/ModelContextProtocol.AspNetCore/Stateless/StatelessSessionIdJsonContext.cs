using System.Text.Json.Serialization;

namespace ModelContextProtocol.AspNetCore.Stateless;

[JsonSerializable(typeof(StatelessSessionId))]
internal sealed partial class StatelessSessionIdJsonContext : JsonSerializerContext;
