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
        var scopes = connection?.ScopeCsv.ToList() ?? [];
        var scopeSet = scopes.ToHashSet(StringComparer.OrdinalIgnoreCase);

        var features = new CapabilityFeatures(
            userinfo: _options.Features.UserInfo && scopeSet.Contains("openid"),
            publish_member_post: _options.Features.MemberPosting && scopeSet.Contains("w_member_social"),
            publish_org_post: _options.Features.OrganizationPosting &&
                (scopeSet.Contains("w_organization_social") || scopeSet.Contains("rw_organization_admin")),
            ads_management: _options.Features.AdsManagement && scopeSet.Overlaps(["rw_ads", "r_ads"]),
            ads_reporting: _options.Features.AdsReporting && scopeSet.Contains("r_ads_reporting"),
            webhooks: _options.Features.Webhooks);

        return new CapabilitySnapshot(
            connected: connection is { Status: "active" },
            auth: new AuthSnapshot(
                type: connection?.AuthType ?? "oidc",
                hasRefreshToken: connection?.HasRefreshToken ?? false,
                expiresAtUtc: connection?.AccessTokenExpiresAtUtc),
            scopes: scopes,
            features: features);
    }
}
