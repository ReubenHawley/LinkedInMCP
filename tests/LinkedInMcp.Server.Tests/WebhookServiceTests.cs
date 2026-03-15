using LinkedInMcp.Core.Configuration;
using LinkedInMcp.Core.Data;
using LinkedInMcp.Core.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace LinkedInMcp.Server.Tests;

public sealed class WebhookServiceTests
{
    [Fact]
    public void ValidateSignature_AcceptsKnownGoodSignature()
    {
        var options = Options.Create(new LinkedInOptions
        {
            ClientSecret = "super-secret"
        });

        using var dbContext = CreateDbContext();
        var backgroundJobs = new LinkedInBackgroundJobService(dbContext, TimeProvider.System, NullLogger<LinkedInBackgroundJobService>.Instance);
        var service = new LinkedInWebhookService(dbContext, backgroundJobs, options);

        const string body = "{\"notificationId\":\"abc123\"}";
        var expectedSignature = $"hmacsha256={service.CreateChallengeResponse(body)}";

        Assert.True(service.ValidateSignature(body, expectedSignature));
    }

    private static LinkedInMcpDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<LinkedInMcpDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("n"))
            .Options;

        return new LinkedInMcpDbContext(options);
    }
}
