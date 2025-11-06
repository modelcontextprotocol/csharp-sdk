using ModelContextProtocol.Server;

namespace AspNetCoreMcpServer.EventStore;

public class EventStoreCleanupService : BackgroundService
{
    private readonly TimeSpan _jobRunFrequencyInMinutes;
    private readonly ILogger<EventStoreCleanupService> _logger;
    private readonly IEventStoreCleaner? _eventStoreCleaner;

    public EventStoreCleanupService(ILogger<EventStoreCleanupService> logger, IConfiguration configuration, IEventStoreCleaner? eventStoreCleaner = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        
        _eventStoreCleaner = eventStoreCleaner;
        _jobRunFrequencyInMinutes = TimeSpan.FromMinutes(configuration.GetValue<int>("EventStore:CleanupJobRunFrequencyInMinutes", 30));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {

        if (_eventStoreCleaner is null)
        {
            _logger.LogWarning("No event store cleaner implementation provided. Event store cleanup job will not run.");
            return;
        }

        _logger.LogInformation("Event store cleanup job started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                _logger.LogInformation("Running event store cleanup job at {CurrentTimeInUtc}.", DateTime.UtcNow);
                _eventStoreCleaner.CleanEventStore();
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
