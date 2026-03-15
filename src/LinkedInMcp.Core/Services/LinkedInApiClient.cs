using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using LinkedInMcp.Core.Configuration;
using LinkedInMcp.Core.Models;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LinkedInMcp.Core.Services;

public sealed class LinkedInApiClient(
    IHttpClientFactory httpClientFactory,
    IOptions<LinkedInOptions> options,
    ILogger<LinkedInApiClient> logger)
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly LinkedInOptions _options = options.Value;

    public async Task<UserInfoDocument> GetUserInfoAsync(string accessToken, CancellationToken cancellationToken)
    {
        using var response = await SendApiAsync(
            HttpMethod.Get,
            "v2/userinfo",
            accessToken,
            cancellationToken: cancellationToken);
        using var document = await ReadJsonAsync(response, cancellationToken) ?? JsonDocument.Parse("{}");

        return new UserInfoDocument(
            GetString(document.RootElement, "sub") ?? throw new InvalidOperationException("LinkedIn userinfo did not include 'sub'."),
            GetString(document.RootElement, "name"),
            GetString(document.RootElement, "email"));
    }

    public async Task<TokenIntrospectionDocument> IntrospectTokenAsync(
        string accessToken,
        string clientId,
        string clientSecret,
        CancellationToken cancellationToken)
    {
        using var client = httpClientFactory.CreateClient("linkedin-auth");
        using var response = await client.PostAsync(
            BuildUri(_options.AuthBaseUrl, "oauth/v2/introspectToken"),
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["token"] = accessToken,
                ["client_id"] = clientId,
                ["client_secret"] = clientSecret
            }),
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw await CreateExceptionAsync(response, cancellationToken);
        }

        using var document = await ReadJsonAsync(response, cancellationToken) ?? JsonDocument.Parse("{}");
        var scopes = (GetString(document.RootElement, "scope") ?? string.Empty)
            .Split(' ', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var expiresAtUnix = GetLong(document.RootElement, "expires_at");

        return new TokenIntrospectionDocument(
            active: GetBoolean(document.RootElement, "active") ?? false,
            authType: GetString(document.RootElement, "auth_type") ?? "oauth",
            scopes,
            expiresAtUnix.HasValue ? DateTimeOffset.FromUnixTimeSeconds(expiresAtUnix.Value) : null);
    }

    public async Task<IReadOnlyList<OrganizationAclRecord>> GetOrganizationAccessAsync(string accessToken, CancellationToken cancellationToken)
    {
        var results = new List<OrganizationAclRecord>();
        var start = 0;

        while (true)
        {
            using var response = await SendRestAsync(
                HttpMethod.Get,
                "organizationAcls",
                accessToken,
                query: new Dictionary<string, string?>
                {
                    ["q"] = "roleAssignee",
                    ["roleAssignee"] = "urn:li:person:self",
                    ["state"] = "APPROVED",
                    ["start"] = start.ToString(),
                    ["count"] = "100"
                },
                cancellationToken: cancellationToken);
            using var document = await ReadJsonAsync(response, cancellationToken) ?? JsonDocument.Parse("{}");

            var elements = TryGetProperty(document.RootElement, "elements");
            if (elements is null || elements.Value.ValueKind != JsonValueKind.Array || elements.Value.GetArrayLength() == 0)
            {
                break;
            }

            foreach (var element in elements.Value.EnumerateArray())
            {
                var organizationUrn =
                    GetString(element, "organization") ??
                    GetString(element, "organizationalTarget") ??
                    GetNestedString(element, "organization", "urn") ??
                    GetNestedString(element, "organizationalTarget", "urn");

                var role =
                    GetString(element, "role") ??
                    GetString(element, "roleType") ??
                    GetNestedString(element, "role", "localizedName");

                if (string.IsNullOrWhiteSpace(organizationUrn) || string.IsNullOrWhiteSpace(role))
                {
                    continue;
                }

                results.Add(new OrganizationAclRecord(
                    organizationUrn,
                    role,
                    GetString(element, "state") ?? "APPROVED"));
            }

            if (!TryGetNextStart(document.RootElement, out start))
            {
                break;
            }
        }

        return results;
    }

    public async Task<OrganizationProfileDocument> GetOrganizationAsync(
        string accessToken,
        string organizationUrn,
        CancellationToken cancellationToken)
    {
        using var response = await SendRestAsync(
            HttpMethod.Get,
            $"organizations/{ExtractNumericId(organizationUrn)}",
            accessToken,
            cancellationToken: cancellationToken);
        using var document = await ReadJsonAsync(response, cancellationToken) ?? JsonDocument.Parse("{}");

        var displayName =
            GetString(document.RootElement, "localizedName") ??
            GetNestedString(document.RootElement, "name", "localized") ??
            GetNestedString(document.RootElement, "localizedName", "value") ??
            GetString(document.RootElement, "vanityName") ??
            organizationUrn;

        return new OrganizationProfileDocument(organizationUrn, displayName);
    }

    public async Task<PostCreationDocument> CreatePostAsync(
        string accessToken,
        string authorType,
        string authorUrn,
        string commentary,
        CancellationToken cancellationToken)
    {
        var payload = new
        {
            author = new
            {
                type = authorType,
                urn = authorUrn
            },
            commentary,
            visibility = "PUBLIC",
            distribution = new
            {
                feedDistribution = "MAIN_FEED"
            }
        };

        using var response = await SendRestAsync(
            HttpMethod.Post,
            "posts",
            accessToken,
            body: payload,
            cancellationToken: cancellationToken);
        using var document = await ReadJsonAsync(response, cancellationToken, allowEmptyDocument: true);

        var postUrn =
            document is null
                ? null
                : GetString(document.RootElement, "postUrn") ??
                  GetString(document.RootElement, "id");

        postUrn ??= response.Headers.TryGetValues("x-restli-id", out var restliIds)
            ? restliIds.FirstOrDefault()
            : null;
        postUrn ??= response.Headers.Location?.ToString();

        if (string.IsNullOrWhiteSpace(postUrn))
        {
            throw new InvalidOperationException("LinkedIn post creation response did not include a post identifier.");
        }

        if (!postUrn.StartsWith("urn:", StringComparison.OrdinalIgnoreCase))
        {
            postUrn = $"urn:li:share:{postUrn.Trim('/')}";
        }

        return new PostCreationDocument(
            postUrn.Split(':').Last(),
            postUrn,
            "rest_posts",
            document?.RootElement.GetRawText());
    }

    public async Task<SocialActionSummaryDocument> GetSocialActionsAsync(
        string accessToken,
        string postUrn,
        CancellationToken cancellationToken)
    {
        using var response = await SendRestAsync(
            HttpMethod.Get,
            $"socialActions/{Uri.EscapeDataString(postUrn)}",
            accessToken,
            cancellationToken: cancellationToken);
        using var document = await ReadJsonAsync(response, cancellationToken) ?? JsonDocument.Parse("{}");

        return new SocialActionSummaryDocument(
            likes: GetNestedLong(document.RootElement, "likesSummary", "totalLikes") ?? 0,
            comments: GetNestedLong(document.RootElement, "commentsSummary", "totalComments") ?? 0,
            shares: GetNestedLong(document.RootElement, "sharesSummary", "totalShares") ?? 0);
    }

    public async Task<OrganizationShareStatisticsDocument> GetOrganizationShareStatisticsAsync(
        string accessToken,
        string organizationUrn,
        string postUrn,
        CancellationToken cancellationToken)
    {
        using var response = await SendRestAsync(
            HttpMethod.Get,
            "organizationalEntityShareStatistics",
            accessToken,
            query: new Dictionary<string, string?>
            {
                ["q"] = "organizationalEntity",
                ["organizationalEntity"] = organizationUrn,
                ["shares"] = $"List({postUrn})"
            },
            cancellationToken: cancellationToken);
        using var document = await ReadJsonAsync(response, cancellationToken) ?? JsonDocument.Parse("{}");

        var elements = TryGetProperty(document.RootElement, "elements");
        if (elements is null || elements.Value.ValueKind != JsonValueKind.Array || elements.Value.GetArrayLength() == 0)
        {
            return new OrganizationShareStatisticsDocument(0, 0, 0, 0);
        }

        var first = elements.Value.EnumerateArray().First();
        var stats = TryGetProperty(first, "totalShareStatistics") ?? first;

        return new OrganizationShareStatisticsDocument(
            impressions: GetLong(stats, "impressionCount") ?? 0,
            uniqueImpressions: GetLong(stats, "uniqueImpressionsCount") ?? 0,
            clicks: GetLong(stats, "clickCount") ?? 0,
            engagement: GetLong(stats, "engagement") ?? 0);
    }

    private async Task<HttpResponseMessage> SendRestAsync(
        HttpMethod method,
        string path,
        string accessToken,
        IReadOnlyDictionary<string, string?>? query = null,
        object? body = null,
        CancellationToken cancellationToken = default)
    {
        using var client = httpClientFactory.CreateClient("linkedin-api");
        using var request = BuildApiRequest(method, path, accessToken, query, body, isRestApi: true);
        var response = await client.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw await CreateExceptionAsync(response, cancellationToken);
        }

        return response;
    }

    private async Task<HttpResponseMessage> SendApiAsync(
        HttpMethod method,
        string path,
        string accessToken,
        IReadOnlyDictionary<string, string?>? query = null,
        object? body = null,
        CancellationToken cancellationToken = default)
    {
        using var client = httpClientFactory.CreateClient("linkedin-api");
        using var request = BuildApiRequest(method, path, accessToken, query, body, isRestApi: false);
        var response = await client.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw await CreateExceptionAsync(response, cancellationToken);
        }

        return response;
    }

    private HttpRequestMessage BuildApiRequest(
        HttpMethod method,
        string path,
        string accessToken,
        IReadOnlyDictionary<string, string?>? query,
        object? body,
        bool isRestApi)
    {
        var relativePath = isRestApi ? $"rest/{path.TrimStart('/')}" : path.TrimStart('/');
        var targetUri = BuildUri(_options.ApiBaseUrl, relativePath);

        if (query is { Count: > 0 })
        {
            targetUri = QueryHelpers.AddQueryString(targetUri, query);
        }

        HttpRequestMessage request;
        if (method == HttpMethod.Get && targetUri.Length >= _options.QueryTunnelThreshold)
        {
            var splitIndex = targetUri.IndexOf('?', StringComparison.Ordinal);
            var tunnelTarget = splitIndex >= 0 ? targetUri[..splitIndex] : targetUri;
            var queryString = splitIndex >= 0 ? targetUri[(splitIndex + 1)..] : string.Empty;

            request = new HttpRequestMessage(HttpMethod.Post, tunnelTarget)
            {
                Content = new StringContent(queryString, Encoding.UTF8, "application/x-www-form-urlencoded")
            };
            request.Headers.TryAddWithoutValidation("X-HTTP-Method-Override", "GET");
        }
        else
        {
            request = new HttpRequestMessage(method, targetUri);
        }

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.TryAddWithoutValidation("X-Restli-Protocol-Version", "2.0.0");
        if (isRestApi)
        {
            request.Headers.TryAddWithoutValidation("Linkedin-Version", _options.DefaultApiVersion);
        }

        if (body is not null)
        {
            request.Content = JsonContent.Create(body, options: SerializerOptions);
        }

        return request;
    }

    private async Task<LinkedInApiException> CreateExceptionAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        string? message = null;
        int? serviceStatus = null;
        string? serviceErrorCode = null;

        if (response.Content.Headers.ContentLength is not 0)
        {
            try
            {
                using var document = await ReadJsonAsync(response, cancellationToken);
                if (document is not null)
                {
                    message = GetString(document.RootElement, "message");
                    serviceStatus = GetLong(document.RootElement, "status") is { } status ? (int)status : null;
                    serviceErrorCode = GetString(document.RootElement, "serviceErrorCode");
                }
            }
            catch (JsonException)
            {
            }
        }

        message ??= response.ReasonPhrase ?? "LinkedIn request failed.";
        var requestId = response.Headers.TryGetValues("x-li-request-id", out var requestIds)
            ? requestIds.FirstOrDefault()
            : null;

        var (code, retryable) = NormalizeErrorCode(response.StatusCode, message);
        logger.LogWarning(
            "LinkedIn upstream failure {StatusCode} {Code} {RequestId}",
            (int)response.StatusCode,
            code,
            requestId);

        return new LinkedInApiException(new LinkedInErrorDetails(
            code,
            serviceErrorCode is null ? message : $"{message} (service error {serviceErrorCode})",
            serviceStatus ?? (int)response.StatusCode,
            requestId,
            retryable));
    }

    private static (string Code, bool Retryable) NormalizeErrorCode(HttpStatusCode statusCode, string message)
    {
        var normalizedMessage = message.ToLowerInvariant();

        return statusCode switch
        {
            HttpStatusCode.Unauthorized when normalizedMessage.Contains("expired") => ("TOKEN_EXPIRED", false),
            HttpStatusCode.Unauthorized when normalizedMessage.Contains("revoked") => ("TOKEN_REVOKED", false),
            HttpStatusCode.Unauthorized => ("UNAUTHORIZED", false),
            HttpStatusCode.Forbidden when normalizedMessage.Contains("scope") => ("INSUFFICIENT_SCOPE", false),
            HttpStatusCode.Forbidden => ("FORBIDDEN", false),
            (HttpStatusCode)426 => ("UPSTREAM_VERSION_DEPRECATED", false),
            (HttpStatusCode)429 => ("RATE_LIMITED", true),
            HttpStatusCode.InternalServerError => ("UPSTREAM_UNAVAILABLE", true),
            HttpStatusCode.GatewayTimeout => ("UPSTREAM_UNAVAILABLE", true),
            _ => ("LINKEDIN_ERROR", false)
        };
    }

    private static async Task<JsonDocument?> ReadJsonAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken,
        bool allowEmptyDocument = false)
    {
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(payload))
        {
            return allowEmptyDocument ? null : JsonDocument.Parse("{}");
        }

        return JsonDocument.Parse(payload);
    }

    private static string BuildUri(string baseUrl, string relativePath)
        => $"{baseUrl.TrimEnd('/')}/{relativePath.TrimStart('/')}";

    private static string? GetString(JsonElement element, string propertyName)
        => TryGetProperty(element, propertyName) is { } value && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static long? GetLong(JsonElement element, string propertyName)
        => TryGetProperty(element, propertyName) is { } value && value.TryGetInt64(out var result)
            ? result
            : null;

    private static bool? GetBoolean(JsonElement element, string propertyName)
        => TryGetProperty(element, propertyName) is { } value && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : null;

    private static string? GetNestedString(JsonElement element, string propertyName, string nestedPropertyName)
        => TryGetProperty(element, propertyName) is { } value
            ? GetString(value, nestedPropertyName)
            : null;

    private static long? GetNestedLong(JsonElement element, string propertyName, string nestedPropertyName)
        => TryGetProperty(element, propertyName) is { } value
            ? GetLong(value, nestedPropertyName)
            : null;

    private static JsonElement? TryGetProperty(JsonElement element, string propertyName)
        => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(propertyName, out var value)
            ? value
            : null;

    private static bool TryGetNextStart(JsonElement root, out int start)
    {
        start = 0;

        var paging = TryGetProperty(root, "paging");
        var links = paging is { } pagingValue ? TryGetProperty(pagingValue, "links") : null;
        if (links is null || links.Value.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        foreach (var link in links.Value.EnumerateArray())
        {
            if (!string.Equals(GetString(link, "rel"), "next", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var href = GetString(link, "href");
            if (string.IsNullOrWhiteSpace(href))
            {
                continue;
            }

            var queryIndex = href.IndexOf('?', StringComparison.Ordinal);
            var query = queryIndex >= 0 ? href[(queryIndex + 1)..] : href;
            var parsed = QueryHelpers.ParseQuery(query);
            if (parsed.TryGetValue("start", out var values) && int.TryParse(values.FirstOrDefault(), out start))
            {
                return true;
            }
        }

        return false;
    }

    private static string ExtractNumericId(string organizationUrn)
        => organizationUrn.Split(':').Last();
}

public sealed record UserInfoDocument(string Subject, string? Name, string? Email);

public sealed record TokenIntrospectionDocument(
    bool active,
    string authType,
    IReadOnlyList<string> scopes,
    DateTimeOffset? expiresAtUtc);

public sealed record OrganizationAclRecord(string OrganizationUrn, string Role, string State);

public sealed record OrganizationProfileDocument(string OrganizationUrn, string DisplayName);

public sealed record PostCreationDocument(string PostId, string PostUrn, string UpstreamMode, string? RawResponseJson);

public sealed record SocialActionSummaryDocument(long likes, long comments, long shares);

public sealed record OrganizationShareStatisticsDocument(long impressions, long uniqueImpressions, long clicks, long engagement);
