namespace ModelContextProtocol.AspNetCore.Stateless;

internal sealed record UserIdClaim(string Type, string Value, string Issuer);
