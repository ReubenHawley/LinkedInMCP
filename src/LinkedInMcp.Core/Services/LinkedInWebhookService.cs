using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LinkedInMcp.Core.Configuration;
using LinkedInMcp.Core.Data;
using LinkedInMcp.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace LinkedInMcp.Core.Services;

public sealed class LinkedInWebhookService(
    LinkedInMcpDbContext dbContext,
    LinkedInBackgroundJobService backgroundJobs,
    IOptions<LinkedInOptions> options)
{
    private readonly LinkedInOptions _options = options.Value;

    public string CreateChallengeResponse(string challengeCode)
    {
        if (string.IsNullOrWhiteSpace(_options.ClientSecret))
        {
            throw new InvalidOperationException("LinkedIn client secret is required for webhook validation.");
        }

        var challengeBytes = Encoding.UTF8.GetBytes(challengeCode);
        var secretBytes = Encoding.UTF8.GetBytes(_options.ClientSecret);
        var hash = HMACSHA256.HashData(secretBytes, challengeBytes);

        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public bool ValidateSignature(string requestBody, string? signature)
    {
        if (string.IsNullOrWhiteSpace(signature) || string.IsNullOrWhiteSpace(_options.ClientSecret))
        {
            return false;
        }

        var normalizedSignature = signature.StartsWith("hmacsha256=", StringComparison.OrdinalIgnoreCase)
            ? signature["hmacsha256=".Length..]
            : signature;

        var bodyBytes = Encoding.UTF8.GetBytes(requestBody);
        var secretBytes = Encoding.UTF8.GetBytes(_options.ClientSecret);
        var hash = HMACSHA256.HashData(secretBytes, bodyBytes);
        var computedSignature = Convert.ToHexString(hash).ToLowerInvariant();

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(computedSignature),
            Encoding.UTF8.GetBytes(normalizedSignature.ToLowerInvariant()));
    }

    public async Task<WebhookReceipt> RecordDeliveryAsync(
        string requestBody,
        string? signature,
        CancellationToken cancellationToken)
    {
        var notificationId = TryExtractNotificationId(requestBody) ?? Guid.NewGuid().ToString("n");
        var existing = await dbContext.WebhookDeliveries
            .SingleOrDefaultAsync(delivery => delivery.NotificationId == notificationId, cancellationToken);

        if (existing is not null)
        {
            return new WebhookReceipt(existing.Id, "duplicate");
        }

        var signatureValid = ValidateSignature(requestBody, signature);

        var delivery = new WebhookDelivery
        {
            NotificationId = notificationId,
            PayloadJson = requestBody,
            SignatureValid = signatureValid
        };

        dbContext.WebhookDeliveries.Add(delivery);
        await dbContext.SaveChangesAsync(cancellationToken);

        if (signatureValid)
        {
            await backgroundJobs.EnqueueAsync(
                "webhook-delivery",
                new LinkedInBackgroundJobService.WebhookJobPayload(delivery.Id),
                cancellationToken);
        }

        return new WebhookReceipt(delivery.Id, signatureValid ? "accepted" : "invalid-signature");
    }

    private static string? TryExtractNotificationId(string payloadJson)
    {
        using var document = JsonDocument.Parse(payloadJson);
        var root = document.RootElement;

        if (root.TryGetProperty("notificationId", out var notificationId))
        {
            return notificationId.GetString();
        }

        if (root.TryGetProperty("elements", out var elements) &&
            elements.ValueKind == JsonValueKind.Array &&
            elements.GetArrayLength() > 0 &&
            elements[0].TryGetProperty("notificationId", out var nestedNotificationId))
        {
            return nestedNotificationId.GetString();
        }

        return null;
    }
}
