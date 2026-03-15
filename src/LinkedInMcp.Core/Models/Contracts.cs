namespace LinkedInMcp.Core.Models;

public sealed record CapabilityFeatures(
    bool userinfo,
    bool publish_member_post,
    bool publish_org_post,
    bool post_analytics,
    bool ads_management,
    bool ads_reporting,
    bool webhooks);

public sealed record CapabilitySnapshot(
    bool connected,
    AuthSnapshot auth,
    IReadOnlyList<string> scopes,
    CapabilityFeatures features);

public sealed record AuthSnapshot(
    string type,
    bool hasRefreshToken,
    DateTimeOffset? expiresAtUtc);

public sealed record AuthUrlResponse(
    string authorizationUrl,
    string browserReadyAuthorizationUrl,
    string copyInstructions,
    string redirectUri,
    string configurationHint,
    string state,
    IReadOnlyList<string> scopes,
    DateTimeOffset expiresAtUtc);

public sealed record ConnectionStatusResponse(
    Guid? connectionId,
    string status,
    string? subjectKey,
    string? displayName,
    string? email,
    CapabilitySnapshot capabilities);

public sealed record LinkedInProfileSnapshot(
    Guid connectionId,
    string subjectKey,
    string displayName,
    string? email,
    IReadOnlyList<string> scopes,
    DateTimeOffset updatedAtUtc);

public sealed record OrganizationAccessSnapshot(
    Guid connectionId,
    string organizationUrn,
    string displayName,
    IReadOnlyList<string> roles,
    DateTimeOffset lastSyncedAtUtc,
    string syncStatus);

public sealed record OrganizationSyncResult(
    string status,
    Guid connectionId,
    int organizationCount,
    int roleCount,
    DateTimeOffset syncedAtUtc,
    LinkedInErrorDetails? error = null);

public sealed record PostPublishResult(
    string status,
    Guid connectionId,
    string? postId,
    string? postUrn,
    string authorType,
    string authorUrn,
    string? organizationUrn,
    DateTimeOffset? publishedAtUtc,
    string? upstreamMode,
    LinkedInErrorDetails? error = null);

public sealed record PostAnalyticsSnapshot(
    string status,
    Guid? connectionId,
    string postId,
    string? postUrn,
    string authorType,
    string authorUrn,
    string? organizationUrn,
    IReadOnlyDictionary<string, long> metrics,
    DateTimeOffset retrievedAtUtc,
    LinkedInErrorDetails? error = null);

public sealed record LinkedInErrorDetails(
    string code,
    string message,
    int? upstreamStatus,
    string? upstreamRequestId,
    bool retryable);

public sealed record OperationResult(
    string status,
    string message);

public sealed record DeletionResult(
    Guid connectionId,
    string status,
    DateTimeOffset requestedAtUtc);

public sealed record WebhookReceipt(
    Guid deliveryId,
    string status);
