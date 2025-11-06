namespace AspNetCoreMcpServer.EventStore;

/// <summary>
/// Interface for cleaning up the event store
/// </summary>
public interface IEventStoreCleaner
{

    /// <summary>
    /// Cleans up the event store by removing outdated or unnecessary events.
    /// </summary>
    /// <remarks>This method is typically used to maintain the event store's size and performance by clearing
    /// events that are no longer needed.</remarks>
    void CleanEventStore();
}
