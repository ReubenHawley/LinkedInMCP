using LinkedInMcp.Core.Data;
using LinkedInMcp.Core.Models;

namespace LinkedInMcp.Core.Services;

public sealed class LinkedInAnalyticsService(
    LinkedInApiClient apiClient,
    TimeProvider timeProvider)
{
    public async Task<PostAnalyticsSnapshot> GetAnalyticsAsync(
        LinkedInConnection connection,
        string accessToken,
        PublishedLinkedInPost? postRecord,
        string postIdOrUrn,
        CancellationToken cancellationToken)
    {
        var postUrn = postRecord?.PostUrn ?? ResolvePostUrn(postIdOrUrn);
        if (string.IsNullOrWhiteSpace(postUrn))
        {
            return new PostAnalyticsSnapshot(
                "error",
                connection.Id,
                postIdOrUrn,
                null,
                "unknown",
                connection.GetMemberUrn(),
                null,
                new Dictionary<string, long>(),
                timeProvider.GetUtcNow(),
                new LinkedInErrorDetails(
                    "POST_NOT_FOUND",
                    "A stored post receipt is required when the input is not a LinkedIn post URN.",
                    null,
                    null,
                    false));
        }

        var social = await apiClient.GetSocialActionsAsync(accessToken, postUrn, cancellationToken);
        var metrics = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase)
        {
            ["likes"] = social.likes,
            ["comments"] = social.comments,
            ["shares"] = social.shares
        };

        var authorType = postRecord?.AuthorType ?? "member";
        var authorUrn = postRecord?.AuthorUrn ?? connection.GetMemberUrn();
        var organizationUrn = postRecord?.OrganizationUrn;

        if (!string.IsNullOrWhiteSpace(organizationUrn))
        {
            var stats = await apiClient.GetOrganizationShareStatisticsAsync(accessToken, organizationUrn, postUrn, cancellationToken);
            metrics["impressions"] = stats.impressions;
            metrics["uniqueImpressions"] = stats.uniqueImpressions;
            metrics["clicks"] = stats.clicks;
            metrics["engagement"] = stats.engagement;
            authorType = "organization";
            authorUrn = organizationUrn;
        }

        return new PostAnalyticsSnapshot(
            "ok",
            connection.Id,
            postRecord?.ExternalPostId ?? postUrn.Split(':').Last(),
            postUrn,
            authorType,
            authorUrn,
            organizationUrn,
            metrics,
            timeProvider.GetUtcNow());
    }

    private static string? ResolvePostUrn(string postIdOrUrn)
        => postIdOrUrn.StartsWith("urn:li:", StringComparison.OrdinalIgnoreCase) ? postIdOrUrn : null;
}
