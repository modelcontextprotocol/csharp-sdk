namespace ModelContextProtocol.Protocol;

/// <summary>
/// Represents the parameters used with a <see cref="NotificationMethods.RootsListChangedNotification"/>
/// notification from the client to the server, informing it that the list of roots has changed.
/// </summary>
/// <remarks>
/// <para>
/// This notification can be issued by clients without any previous subscription from the server.
/// </para>
/// <para>
/// See the <see href="https://github.com/modelcontextprotocol/specification/blob/main/schema/">schema</see> for details.
/// </para>
/// </remarks>
public sealed class RootsListChangedNotificationParams : NotificationParams;
