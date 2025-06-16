namespace ModelContextProtocol.Protocol;

/// <summary>
/// Represents the result of a <see cref="RequestMethods.Ping"/> request in the Model Context Protocol.
/// </summary>
/// <remarks>
/// <para>
/// The <see cref="PingResult"/> is returned in response to a <see cref="RequestMethods.Ping"/> request, 
/// which is used to verify that the connection between client and server is still alive and responsive. 
/// Since this is a simple connectivity check, the result is an empty object containing no data.
/// </para>
/// <para>
/// Ping requests can be initiated by either the client or the server to check if the other party
/// is still responsive.
/// </para>
/// </remarks>
public sealed class PingResult : Result;