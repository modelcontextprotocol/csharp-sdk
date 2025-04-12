using ModelContextProtocol.Protocol.Transport;
using System.Security.Claims;

namespace ModelContextProtocol.AspNetCore;

internal class HttpMcpSession
{
    public HttpMcpSession(SseResponseStreamTransport transport, ClaimsPrincipal user)
    {
        Transport = transport;
        UserIdClaim = GetUserIdClaim(user);
    }

    public SseResponseStreamTransport Transport { get; }
    public (string ClaimType, string ClaimValue)? UserIdClaim { get; }

    public bool HasSameUserId(ClaimsPrincipal user)
        => UserIdClaim?.ClaimValue == GetUserIdClaim(user)?.ClaimValue;

    // SignalR only checks for ClaimTypes.NameIdentifier in HttpConnectionDispatcher, but AspNetCore.Antiforgery checks that plus the sub and UPN claims.
    // However, we short-circuit unlike antiforgery since we expect to call this to verify MCP messages a lot more frequently than
    // verifying antiforgery tokens from <form> posts.
    private static (string ClaimType, string ClaimValue)? GetUserIdClaim(ClaimsPrincipal user)
    {
        if (user?.Identity?.IsAuthenticated != true)
        {
            return null;
        }

        var claim = user.FindFirst(ClaimTypes.NameIdentifier) ?? user.FindFirst("sub") ?? user.FindFirst(ClaimTypes.Upn);

        if (claim is { } idClaim)
        {
            return (idClaim.Type, idClaim.Value);
        }

        return null;
    }
}
