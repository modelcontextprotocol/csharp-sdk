namespace ModelContextProtocol.Protocol;

/// <summary>
/// Represents the parameters used with a <see cref="NotificationMethods.RootsListChangedNotification"/>
/// notification from the client to the server, informing it that the list of roots has changed.
/// </summary>
/// <remarks>
/// <para>
/// This may be issued by servers without any previous subscription from the client.
/// </para>
/// <para>
/// See the <see href="https://github.com/modelcontextprotocol/specification/blob/main/schema/">schema</see> for details.
/// </para>
/// </remarks>
public sealed class RootsListChangedNotificationParams : NotificationParams;
