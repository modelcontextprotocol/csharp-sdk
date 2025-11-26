namespace ModelContextProtocol.Protocol;

/// <summary>
/// Represents the parameters used with a <see cref="NotificationMethods.PromptListChangedNotification"/>
/// notification from the server to the client, informing it that the list of prompts it offers has changed.
/// </summary>
/// <remarks>
/// <para>
/// This notification can be issued by servers without any previous subscription from the client.
/// </para>
/// <para>
/// See the <see href="https://github.com/modelcontextprotocol/specification/blob/main/schema/">schema</see> for details.
/// </para>
/// </remarks>
public sealed class PromptListChangedNotificationParams : NotificationParams;
