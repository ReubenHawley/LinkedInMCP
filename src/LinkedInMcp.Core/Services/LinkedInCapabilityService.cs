using LinkedInMcp.Core.Configuration;
using LinkedInMcp.Core.Data;
using LinkedInMcp.Core.Models;
using Microsoft.Extensions.Options;

namespace LinkedInMcp.Core.Services;

public sealed class LinkedInCapabilityService(IOptions<LinkedInOptions> options)
{
    private readonly LinkedInOptions _options = options.Value;

    public CapabilitySnapshot BuildSnapshot(LinkedInConnection? connection)
    {
        var scopes = connection?.GetEffectiveScopes() ?? [];
        var scopeSet = scopes.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var tokenActive = connection is null ||
            string.IsNullOrWhiteSpace(connection.TokenStatus) ||
            string.Equals(connection.TokenStatus, "active", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(connection.TokenStatus, "unknown", StringComparison.OrdinalIgnoreCase);

        var features = new CapabilityFeatures(
            userinfo: tokenActive && _options.Features.UserInfo && scopeSet.Contains("openid"),
            publish_member_post: tokenActive && _options.Features.MemberPosting && scopeSet.Contains("w_member_social"),
            publish_org_post: _options.Features.OrganizationPosting &&
                tokenActive &&
                (scopeSet.Contains("w_organization_social") || scopeSet.Contains("rw_organization_admin")),
            post_analytics: tokenActive &&
                _options.Features.PostAnalytics &&
                scopeSet.Overlaps(["r_organization_social", "rw_organization_admin", "w_member_social", "w_organization_social"]),
            ads_management: tokenActive && _options.Features.AdsManagement && scopeSet.Overlaps(["rw_ads", "r_ads"]),
            ads_reporting: tokenActive && _options.Features.AdsReporting && scopeSet.Contains("r_ads_reporting"),
            webhooks: _options.Features.Webhooks);

        return new CapabilitySnapshot(
            connected: connection is { Status: "active" } && tokenActive,
            auth: new AuthSnapshot(
                type: connection?.AuthType ?? "oidc",
                hasRefreshToken: connection?.HasRefreshToken ?? false,
                expiresAtUtc: connection?.AccessTokenExpiresAtUtc),
            scopes: scopes,
            features: features);
    }
}
