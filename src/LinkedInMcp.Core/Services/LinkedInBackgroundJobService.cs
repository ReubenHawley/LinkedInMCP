using System.Text.Json;
using LinkedInMcp.Core.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace LinkedInMcp.Core.Services;

public sealed class LinkedInBackgroundJobService(
    LinkedInMcpDbContext dbContext,
    TimeProvider timeProvider,
    ILogger<LinkedInBackgroundJobService> logger)
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public async Task<Guid> EnqueueAsync(string jobType, object payload, CancellationToken cancellationToken)
    {
        var job = new BackgroundJob
        {
            JobType = jobType,
            PayloadJson = JsonSerializer.Serialize(payload, SerializerOptions),
            AvailableAtUtc = timeProvider.GetUtcNow()
        };

        dbContext.BackgroundJobs.Add(job);
        await dbContext.SaveChangesAsync(cancellationToken);

        return job.Id;
    }

    public async Task<IReadOnlyList<BackgroundJob>> LeaseDueJobsAsync(int maxCount, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();

        var jobs = await dbContext.BackgroundJobs
            .Where(job => job.Status == "queued" && job.AvailableAtUtc <= now)
            .OrderBy(job => job.AvailableAtUtc)
            .Take(maxCount)
            .ToListAsync(cancellationToken);

        foreach (var job in jobs)
        {
            job.Status = "processing";
            job.LockedAtUtc = now;
            job.Attempts++;
        }

        if (jobs.Count > 0)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return jobs;
    }

    public async Task CompleteAsync(Guid jobId, CancellationToken cancellationToken)
    {
        var job = await dbContext.BackgroundJobs.FindAsync([jobId], cancellationToken);
        if (job is null)
        {
            return;
        }

        job.Status = "completed";
        job.CompletedAtUtc = timeProvider.GetUtcNow();
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task FailAsync(Guid jobId, Exception exception, CancellationToken cancellationToken)
    {
        logger.LogWarning(exception, "Background job {JobId} failed", jobId);

        var job = await dbContext.BackgroundJobs.FindAsync([jobId], cancellationToken);
        if (job is null)
        {
            return;
        }

        job.Status = job.Attempts >= 5 ? "failed" : "queued";
        job.AvailableAtUtc = timeProvider.GetUtcNow().AddMinutes(Math.Min(job.Attempts, 5));
        job.LastError = exception.Message.Length > 1024
            ? exception.Message[..1024]
            : exception.Message;

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task ProcessJobAsync(BackgroundJob job, CancellationToken cancellationToken)
    {
        switch (job.JobType)
        {
            case "webhook-delivery":
                await MarkWebhookProcessedAsync(job.PayloadJson, cancellationToken);
                break;
            case "deletion-request":
                await CompleteDeletionAsync(job.PayloadJson, cancellationToken);
                break;
            default:
                logger.LogInformation("Skipping unsupported background job type {JobType}", job.JobType);
                break;
        }
    }

    private async Task MarkWebhookProcessedAsync(string payloadJson, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Deserialize<WebhookJobPayload>(payloadJson, SerializerOptions)
            ?? throw new InvalidOperationException("Webhook payload was missing.");

        var delivery = await dbContext.WebhookDeliveries.FindAsync([payload.DeliveryId], cancellationToken)
            ?? throw new InvalidOperationException("Webhook delivery was not found.");

        delivery.ProcessedAtUtc = timeProvider.GetUtcNow();
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task CompleteDeletionAsync(string payloadJson, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Deserialize<DeletionJobPayload>(payloadJson, SerializerOptions)
            ?? throw new InvalidOperationException("Deletion payload was missing.");

        var request = await dbContext.DeletionRequests.FindAsync([payload.DeletionRequestId], cancellationToken)
            ?? throw new InvalidOperationException("Deletion request was not found.");

        request.Status = "completed";
        request.CompletedAtUtc = timeProvider.GetUtcNow();
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public sealed record WebhookJobPayload(Guid DeliveryId);

    public sealed record DeletionJobPayload(Guid DeletionRequestId);
}
