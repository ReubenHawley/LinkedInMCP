using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace LinkedInMcp.Core.Data;

public sealed class LinkedInConnection
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [MaxLength(256)]
    public string SubjectKey { get; set; } = string.Empty;

    [MaxLength(256)]
    public string DisplayName { get; set; } = string.Empty;

    [MaxLength(256)]
    public string? Email { get; set; }

    [MaxLength(32)]
    public string AuthType { get; set; } = "oidc";

    [MaxLength(32)]
    public string Status { get; set; } = "pending";

    public string ScopeCsv { get; set; } = string.Empty;

    public string ValidatedScopeCsv { get; set; } = string.Empty;

    public string? AccessTokenProtected { get; set; }

    public string? RefreshTokenProtected { get; set; }

    public bool HasRefreshToken { get; set; }

    [MaxLength(32)]
    public string TokenStatus { get; set; } = "unknown";

    [MaxLength(256)]
    public string MemberUrn { get; set; } = string.Empty;

    public DateTimeOffset? AccessTokenExpiresAtUtc { get; set; }

    public DateTimeOffset? RefreshTokenExpiresAtUtc { get; set; }

    public DateTimeOffset? LastValidatedAtUtc { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class LinkedInOAuthState
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [MaxLength(128)]
    public string State { get; set; } = string.Empty;

    public string RequestedScopeCsv { get; set; } = string.Empty;

    [MaxLength(512)]
    public string? ReturnUrl { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset ExpiresAtUtc { get; set; }

    public DateTimeOffset? CompletedAtUtc { get; set; }
}

public sealed class LinkedInOrganizationAccess
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [ForeignKey(nameof(Connection))]
    public Guid ConnectionId { get; set; }

    public LinkedInConnection? Connection { get; set; }

    [MaxLength(256)]
    public string OrganizationUrn { get; set; } = string.Empty;

    [MaxLength(256)]
    public string DisplayName { get; set; } = string.Empty;

    public string RolesCsv { get; set; } = string.Empty;

    [MaxLength(32)]
    public string SyncStatus { get; set; } = "cached";

    public string? SourceMetadataJson { get; set; }

    public DateTimeOffset LastSyncedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class PublishedLinkedInPost
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [ForeignKey(nameof(Connection))]
    public Guid ConnectionId { get; set; }

    public LinkedInConnection? Connection { get; set; }

    [MaxLength(128)]
    public string ExternalPostId { get; set; } = string.Empty;

    [MaxLength(256)]
    public string PostUrn { get; set; } = string.Empty;

    [MaxLength(32)]
    public string AuthorType { get; set; } = "member";

    [MaxLength(256)]
    public string AuthorUrn { get; set; } = string.Empty;

    [MaxLength(256)]
    public string? OrganizationUrn { get; set; }

    [MaxLength(32)]
    public string Status { get; set; } = "published";

    [MaxLength(32)]
    public string UpstreamMode { get; set; } = "rest_posts";

    [MaxLength(3000)]
    public string Text { get; set; } = string.Empty;

    public string? RawResponseJson { get; set; }

    public DateTimeOffset PublishedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class RateLimitLedgerEntry
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    public DateOnly DayUtc { get; set; } = DateOnly.FromDateTime(DateTime.UtcNow);

    [MaxLength(128)]
    public string EndpointKey { get; set; } = string.Empty;

    [MaxLength(32)]
    public string BucketType { get; set; } = string.Empty;

    [MaxLength(128)]
    public string BucketId { get; set; } = string.Empty;

    public int Used { get; set; }

    public int? Limit { get; set; }

    public DateTimeOffset? ThrottledUntilUtc { get; set; }
}

public sealed class WebhookDelivery
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [MaxLength(128)]
    public string NotificationId { get; set; } = string.Empty;

    [MaxLength(128)]
    public string Topic { get; set; } = "linkedin";

    public bool SignatureValid { get; set; }

    public string PayloadJson { get; set; } = string.Empty;

    public DateTimeOffset ReceivedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? ProcessedAtUtc { get; set; }
}

public sealed class DeletionRequest
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [ForeignKey(nameof(Connection))]
    public Guid ConnectionId { get; set; }

    public LinkedInConnection? Connection { get; set; }

    [MaxLength(256)]
    public string SubjectKey { get; set; } = string.Empty;

    [MaxLength(32)]
    public string Status { get; set; } = "queued";

    public DateTimeOffset RequestedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? CompletedAtUtc { get; set; }
}

public sealed class BackgroundJob
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [MaxLength(64)]
    public string JobType { get; set; } = string.Empty;

    [MaxLength(32)]
    public string Status { get; set; } = "queued";

    public string PayloadJson { get; set; } = string.Empty;

    public int Attempts { get; set; }

    public DateTimeOffset AvailableAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? LockedAtUtc { get; set; }

    public DateTimeOffset? CompletedAtUtc { get; set; }

    [MaxLength(1024)]
    public string? LastError { get; set; }
}
