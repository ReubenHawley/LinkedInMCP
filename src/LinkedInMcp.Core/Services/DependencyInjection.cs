using LinkedInMcp.Core.Configuration;
using LinkedInMcp.Core.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LinkedInMcp.Core.Services;

public static class DependencyInjection
{
    public static IServiceCollection AddLinkedInMcpCore(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<LinkedInOptions>(configuration.GetSection(LinkedInOptions.SectionName));
        services.AddDataProtection();
        services.AddSingleton(TimeProvider.System);

        services.AddDbContext<LinkedInMcpDbContext>(options =>
        {
            var connectionString = configuration.GetConnectionString("linkedinmcp")
                ?? throw new InvalidOperationException("Connection string 'linkedinmcp' is required.");

            options.UseNpgsql(connectionString);
        });

        services.AddHttpClient("linkedin-auth");
        services.AddHttpClient("linkedin-api", client =>
        {
            client.DefaultRequestHeaders.TryAddWithoutValidation("X-Restli-Protocol-Version", "2.0.0");
            client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "LinkedInMcp/0.1");
        });

        services.AddScoped<LinkedInCapabilityService>();
        services.AddScoped<LinkedInBackgroundJobService>();
        services.AddScoped<LinkedInWebhookService>();
        services.AddScoped<LinkedInConnectionService>();

        return services;
    }

    public static async Task InitializeLinkedInMcpDatabaseAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<LinkedInMcpDbContext>();
        await dbContext.Database.EnsureCreatedAsync(cancellationToken);
    }
}
