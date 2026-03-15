using LinkedInMcp.Core.Data;
using LinkedInMcp.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace LinkedInMcp.Core.Services;

public sealed class LinkedInPostService(
    LinkedInMcpDbContext dbContext,
    LinkedInApiClient apiClient,
    TimeProvider timeProvider)
{
    private static readonly string[] AllowedOrganizationPostingRoles =
    [
        "ADMINISTRATOR",
        "CONTENT_ADMIN",
        "SUPER_ADMIN",
        "DIRECT_SPONSORED_CONTENT_POSTER"
    ];

    public async Task<PostPublishResult> CreateMemberPostAsync(
        LinkedInConnection connection,
        string accessToken,
        string text,
        CancellationToken cancellationToken)
    {
        ValidateText(text);

        var creation = await apiClient.CreatePostAsync(
            accessToken,
            authorType: "member",
            authorUrn: connection.GetMemberUrn(),
            commentary: text,
            cancellationToken);

        var publishedAtUtc = timeProvider.GetUtcNow();
        dbContext.PublishedLinkedInPosts.Add(new PublishedLinkedInPost
        {
            ConnectionId = connection.Id,
            ExternalPostId = creation.PostId,
            PostUrn = creation.PostUrn,
            AuthorType = "member",
            AuthorUrn = connection.GetMemberUrn(),
            Status = "published",
            Text = text,
            UpstreamMode = creation.UpstreamMode,
            RawResponseJson = creation.RawResponseJson,
            PublishedAtUtc = publishedAtUtc
        });

        await dbContext.SaveChangesAsync(cancellationToken);

        return new PostPublishResult(
            "ok",
            connection.Id,
            creation.PostId,
            creation.PostUrn,
            "member",
            connection.GetMemberUrn(),
            organizationUrn: null,
            publishedAtUtc,
            creation.UpstreamMode);
    }

    public async Task<PostPublishResult> CreateOrganizationPostAsync(
        LinkedInConnection connection,
        string accessToken,
        string organizationUrn,
        string text,
        CancellationToken cancellationToken)
    {
        ValidateText(text);

        var organizationAccess = await dbContext.LinkedInOrganizationAccess
            .SingleOrDefaultAsync(
                access => access.ConnectionId == connection.Id && access.OrganizationUrn == organizationUrn,
                cancellationToken);

        if (organizationAccess is null)
        {
            return new PostPublishResult(
                "error",
                connection.Id,
                null,
                null,
                "organization",
                organizationUrn,
                organizationUrn,
                publishedAtUtc: null,
                upstreamMode: null,
                new LinkedInErrorDetails(
                    "ORG_ACCESS_NOT_SYNCED",
                    "Organization access has not been synced for this connection.",
                    null,
                    null,
                    false));
        }

        var roles = organizationAccess.RolesCsv.ToList();
        if (!roles.Any(role => AllowedOrganizationPostingRoles.Contains(role, StringComparer.OrdinalIgnoreCase)))
        {
            return new PostPublishResult(
                "error",
                connection.Id,
                null,
                null,
                "organization",
                organizationUrn,
                organizationUrn,
                publishedAtUtc: null,
                upstreamMode: null,
                new LinkedInErrorDetails(
                    "ORG_ROLE_DENIED",
                    "The connection does not have a role that can publish for this organization.",
                    null,
                    null,
                    false));
        }

        var creation = await apiClient.CreatePostAsync(
            accessToken,
            authorType: "organization",
            authorUrn: organizationUrn,
            commentary: text,
            cancellationToken);

        var publishedAtUtc = timeProvider.GetUtcNow();
        dbContext.PublishedLinkedInPosts.Add(new PublishedLinkedInPost
        {
            ConnectionId = connection.Id,
            ExternalPostId = creation.PostId,
            PostUrn = creation.PostUrn,
            AuthorType = "organization",
            AuthorUrn = organizationUrn,
            OrganizationUrn = organizationUrn,
            Status = "published",
            Text = text,
            UpstreamMode = creation.UpstreamMode,
            RawResponseJson = creation.RawResponseJson,
            PublishedAtUtc = publishedAtUtc
        });

        await dbContext.SaveChangesAsync(cancellationToken);

        return new PostPublishResult(
            "ok",
            connection.Id,
            creation.PostId,
            creation.PostUrn,
            "organization",
            organizationUrn,
            organizationUrn,
            publishedAtUtc,
            creation.UpstreamMode);
    }

    private static void ValidateText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new InvalidOperationException("LinkedIn posts must include non-empty text.");
        }

        if (text.Length > 3000)
        {
            throw new InvalidOperationException("LinkedIn text posts cannot exceed 3000 characters.");
        }
    }
}
