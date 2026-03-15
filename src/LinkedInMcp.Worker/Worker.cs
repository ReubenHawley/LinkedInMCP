using LinkedInMcp.Core.Services;

namespace LinkedInMcp.Worker;

public sealed class Worker(
    IServiceScopeFactory serviceScopeFactory,
    ILogger<Worker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = serviceScopeFactory.CreateAsyncScope();
                var jobService = scope.ServiceProvider.GetRequiredService<LinkedInBackgroundJobService>();
                var jobs = await jobService.LeaseDueJobsAsync(10, stoppingToken);

                foreach (var job in jobs)
                {
                    try
                    {
                        await jobService.ProcessJobAsync(job, stoppingToken);
                        await jobService.CompleteAsync(job.Id, stoppingToken);
                    }
                    catch (Exception exception)
                    {
                        await jobService.FailAsync(job.Id, exception, stoppingToken);
                    }
                }
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Background worker loop failed.");
            }

            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }
    }
}
