namespace LinkedInMcp.Core.Configuration;

public sealed class LinkedInOptions
{
    public const string SectionName = "LinkedIn";

    public string ClientId { get; set; } = string.Empty;

    public string ClientSecret { get; set; } = string.Empty;

    public string RedirectUri { get; set; } = string.Empty;

    public string ApiBaseUrl { get; set; } = "https://api.linkedin.com";

    public string AuthBaseUrl { get; set; } = "https://www.linkedin.com";

    public string DefaultApiVersion { get; set; } = "202602";

    public int QueryTunnelThreshold { get; set; } = 1800;

    public string[] DefaultScopes { get; set; } = ["openid", "profile", "email"];

    public LinkedInFeatureFlags Features { get; set; } = new();
}

public sealed class LinkedInFeatureFlags
{
    public bool UserInfo { get; set; } = true;

    public bool MemberPosting { get; set; }

    public bool OrganizationPosting { get; set; }

    public bool PostAnalytics { get; set; } = true;

    public bool AdsManagement { get; set; }

    public bool AdsReporting { get; set; }

    public bool Webhooks { get; set; } = true;
}
