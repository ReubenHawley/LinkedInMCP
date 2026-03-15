namespace LinkedInMcp.Core.Models;

public sealed record CapabilityFeatures(
    bool userinfo,
    bool publish_member_post,
    bool publish_org_post,
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
    DateTimeOffset lastSyncedAtUtc);

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
