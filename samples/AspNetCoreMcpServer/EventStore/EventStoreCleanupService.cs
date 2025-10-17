using ModelContextProtocol.Server;

namespace AspNetCoreMcpServer.EventStore;

public class EventStoreCleanupService : BackgroundService
{
    private readonly TimeSpan _jobRunFrequencyInMinutes;
    private readonly ILogger<EventStoreCleanupService> _logger;
    private readonly IEventStore? _eventStore;

    public EventStoreCleanupService(ILogger<EventStoreCleanupService> logger, IConfiguration configuration, IEventStore? eventStore = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        
        _eventStore = eventStore;
        _jobRunFrequencyInMinutes = TimeSpan.FromMinutes(configuration.GetValue<int>("EventStore:CleanupJobRunFrequencyInMinutes", 30));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {

        if (_eventStore is null)
        {
            _logger.LogWarning("No event store implementation provided. Event store cleanup job will not run.");
            return;
        }

        _logger.LogInformation("Event store cleanup job started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                _logger.LogInformation("Running event store cleanup job at {CurrentTimeInUtc}.", DateTime.UtcNow);
                _eventStore.CleanupEventStore();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error running event store cleanup job.");
            }

            await Task.Delay(_jobRunFrequencyInMinutes, stoppingToken);
        }

        _logger.LogInformation("Event store cleanup job stopping.");
    }
}
