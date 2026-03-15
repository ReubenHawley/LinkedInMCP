using LinkedInMcp.Core.Configuration;
using LinkedInMcp.Core.Data;
using LinkedInMcp.Core.Services;
using Microsoft.Extensions.Options;

namespace LinkedInMcp.Server.Tests;

public sealed class CapabilityServiceTests
{
    [Fact]
    public void BuildSnapshot_MapsScopesToEnabledFeatures()
    {
        var options = Options.Create(new LinkedInOptions
        {
            Features = new LinkedInFeatureFlags
            {
                UserInfo = true,
                MemberPosting = true,
                OrganizationPosting = true,
                PostAnalytics = true,
                AdsManagement = true,
                AdsReporting = true,
                Webhooks = true
            }
        });

        var service = new LinkedInCapabilityService(options);

        var snapshot = service.BuildSnapshot(new LinkedInConnection
        {
            Status = "active",
            AuthType = "oidc",
            HasRefreshToken = true,
            ScopeCsv = "openid",
            ValidatedScopeCsv = "openid,w_member_social,rw_organization_admin,r_ads_reporting,r_organization_social",
            TokenStatus = "active"
        });

        Assert.True(snapshot.connected);
        Assert.True(snapshot.features.userinfo);
        Assert.True(snapshot.features.publish_member_post);
        Assert.True(snapshot.features.publish_org_post);
        Assert.True(snapshot.features.post_analytics);
        Assert.True(snapshot.features.ads_reporting);
        Assert.True(snapshot.features.webhooks);
    }
}
