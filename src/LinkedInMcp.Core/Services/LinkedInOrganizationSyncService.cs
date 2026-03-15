using System.Text.Json;
using LinkedInMcp.Core.Data;
using LinkedInMcp.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace LinkedInMcp.Core.Services;

public sealed class LinkedInOrganizationSyncService(
    LinkedInMcpDbContext dbContext,
    LinkedInApiClient apiClient,
    TimeProvider timeProvider)
{
    public async Task<OrganizationSyncResult> SyncOrganizationsAsync(
        LinkedInConnection connection,
        string accessToken,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var aclRecords = await apiClient.GetOrganizationAccessAsync(accessToken, cancellationToken);
        var byOrganization = aclRecords
            .Where(record => string.Equals(record.State, "APPROVED", StringComparison.OrdinalIgnoreCase))
            .GroupBy(record => record.OrganizationUrn, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var existing = await dbContext.LinkedInOrganizationAccess
            .Where(access => access.ConnectionId == connection.Id)
            .ToDictionaryAsync(access => access.OrganizationUrn, StringComparer.OrdinalIgnoreCase, cancellationToken);

        var seenOrganizations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var roleCount = 0;

        foreach (var group in byOrganization)
        {
            seenOrganizations.Add(group.Key);
            var roles = group.Select(record => record.Role).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(static role => role).ToArray();
            roleCount += roles.Length;

            var access = existing.GetValueOrDefault(group.Key);
            if (access is null)
            {
                access = new LinkedInOrganizationAccess
                {
                    ConnectionId = connection.Id,
                    OrganizationUrn = group.Key
                };

                dbContext.LinkedInOrganizationAccess.Add(access);
            }

            OrganizationProfileDocument? organizationProfile = null;
            try
            {
                organizationProfile = await apiClient.GetOrganizationAsync(accessToken, group.Key, cancellationToken);
            }
            catch (LinkedInApiException)
            {
            }

            access.DisplayName = organizationProfile?.DisplayName ?? group.Key;
            access.RolesCsv = roles.ToCsv();
            access.SyncStatus = "synced";
            access.SourceMetadataJson = JsonSerializer.Serialize(group.ToArray());
            access.LastSyncedAtUtc = now;
        }

        var staleOrganizations = existing.Values
            .Where(access => !seenOrganizations.Contains(access.OrganizationUrn))
            .ToArray();
        if (staleOrganizations.Length > 0)
        {
            dbContext.LinkedInOrganizationAccess.RemoveRange(staleOrganizations);
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        return new OrganizationSyncResult("ok", connection.Id, byOrganization.Length, roleCount, now);
    }
}
