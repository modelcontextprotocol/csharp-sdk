using System.Text.Json.Serialization;

namespace ModelContextProtocol.AspNetCore;

[JsonSerializable(typeof(StatelessSessionId))]
internal sealed partial class StatelessSessionIdJsonContext : JsonSerializerContext;
