using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using LinkedInMcp.Core.Configuration;
using LinkedInMcp.Core.Data;
using LinkedInMcp.Core.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace LinkedInMcp.Server.Tests;

public sealed class LinkedInWorkflowTests
{
    [Fact]
    public async Task CompleteOAuthCallbackAsync_PersistsValidatedConnection()
    {
        using var harness = CreateHarness((request, _) => request.RequestUri!.AbsoluteUri switch
        {
            "https://www.linkedin.com/oauth/v2/accessToken" => JsonResponse(new
            {
                access_token = "access-token",
                expires_in = 3600,
                refresh_token = "refresh-token",
                refresh_token_expires_in = 7200
            }),
            "https://www.linkedin.com/oauth/v2/introspectToken" => JsonResponse(new
            {
                active = true,
                auth_type = "oidc",
                scope = "openid w_member_social r_organization_social",
                expires_at = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds()
            }),
            "https://api.linkedin.com/v2/userinfo" => JsonResponse(new
            {
                sub = "member-123",
                name = "Ada Lovelace",
                email = "ada@example.com"
            }),
            _ => throw new InvalidOperationException($"Unexpected request: {request.RequestUri}")
        });

        var auth = await harness.ConnectionService.BeginAuthAsync(["openid", "w_member_social"], null, CancellationToken.None);
        var status = await harness.ConnectionService.CompleteOAuthCallbackAsync(auth.state, "oauth-code", CancellationToken.None);

        Assert.Equal("active", status.status);
        Assert.NotNull(status.connectionId);
        Assert.True(status.capabilities.features.publish_member_post);

        var storedConnection = await harness.DbContext.LinkedInConnections.SingleAsync();
        Assert.Equal("member-123", storedConnection.SubjectKey);
        Assert.Equal("urn:li:person:member-123", storedConnection.MemberUrn);
        Assert.Equal("active", storedConnection.TokenStatus);
        Assert.Contains("w_member_social", storedConnection.ValidatedScopeCsv);
    }

    [Fact]
    public async Task SyncOrganizationsAsync_PopulatesOrganizationCache()
    {
        using var harness = CreateHarness((request, _) =>
        {
            if (request.RequestUri!.AbsoluteUri.StartsWith("https://api.linkedin.com/rest/organizationAcls", StringComparison.Ordinal))
            {
                return JsonResponse(new
                {
                    elements = new object[]
                    {
                        new { organization = "urn:li:organization:1", role = "ADMINISTRATOR", state = "APPROVED" },
                        new { organization = "urn:li:organization:2", role = "CONTENT_ADMIN", state = "APPROVED" }
                    }
                });
            }

            if (request.RequestUri!.AbsoluteUri == "https://api.linkedin.com/rest/organizations/1")
            {
                return JsonResponse(new { localizedName = "Org One" });
            }

            if (request.RequestUri!.AbsoluteUri == "https://api.linkedin.com/rest/organizations/2")
            {
                return JsonResponse(new { localizedName = "Org Two" });
            }

            throw new InvalidOperationException($"Unexpected request: {request.RequestUri}");
        });

        var connection = harness.SeedConnection("openid,rw_organization_admin,w_organization_social");
        var result = await harness.ConnectionService.SyncOrganizationsAsync(connection.Id, CancellationToken.None);

        Assert.Equal("ok", result.status);
        Assert.Equal(2, result.organizationCount);

        var organizations = await harness.DbContext.LinkedInOrganizationAccess.OrderBy(x => x.OrganizationUrn).ToListAsync();
        Assert.Collection(
            organizations,
            first =>
            {
                Assert.Equal("Org One", first.DisplayName);
                Assert.Equal("ADMINISTRATOR", first.RolesCsv);
                Assert.Equal("synced", first.SyncStatus);
            },
            second =>
            {
                Assert.Equal("Org Two", second.DisplayName);
                Assert.Equal("CONTENT_ADMIN", second.RolesCsv);
            });
    }

    [Fact]
    public async Task CreateMemberPostAsync_PersistsPublishedReceipt()
    {
        using var harness = CreateHarness((request, _) =>
        {
            if (request.RequestUri!.AbsoluteUri == "https://api.linkedin.com/rest/posts")
            {
                return JsonResponse(new { id = "urn:li:ugcPost:98765" }, HttpStatusCode.Created);
            }

            throw new InvalidOperationException($"Unexpected request: {request.RequestUri}");
        });

        var connection = harness.SeedConnection("openid,w_member_social");
        var result = await harness.ConnectionService.CreateMemberPostAsync(connection.Id, "Hello LinkedIn", CancellationToken.None);

        Assert.Equal("ok", result.status);
        Assert.Equal("98765", result.postId);

        var storedPost = await harness.DbContext.PublishedLinkedInPosts.SingleAsync();
        Assert.Equal("urn:li:ugcPost:98765", storedPost.PostUrn);
        Assert.Equal("member", storedPost.AuthorType);
    }

    [Fact]
    public async Task CreateOrganizationPostAsync_ReturnsRoleDeniedWithoutPublishableRole()
    {
        using var harness = CreateHarness((_, _) => throw new InvalidOperationException("No upstream call expected."));
        var connection = harness.SeedConnection("rw_organization_admin,w_organization_social");
        harness.DbContext.LinkedInOrganizationAccess.Add(new LinkedInOrganizationAccess
        {
            ConnectionId = connection.Id,
            OrganizationUrn = "urn:li:organization:1",
            DisplayName = "Org One",
            RolesCsv = "VIEWER",
            SyncStatus = "synced"
        });
        await harness.DbContext.SaveChangesAsync();

        var result = await harness.ConnectionService.CreateOrganizationPostAsync(connection.Id, "urn:li:organization:1", "Hello org", CancellationToken.None);

        Assert.Equal("error", result.status);
        Assert.Equal("ORG_ROLE_DENIED", result.error?.code);
    }

    [Fact]
    public async Task GetPostAnalyticsAsync_MergesSocialAndOrganizationMetrics()
    {
        using var harness = CreateHarness((request, _) =>
        {
            if (request.RequestUri!.AbsoluteUri == "https://api.linkedin.com/rest/socialActions/urn%3Ali%3AugcPost%3A98765")
            {
                return JsonResponse(new
                {
                    likesSummary = new { totalLikes = 12 },
                    commentsSummary = new { totalComments = 4 },
                    sharesSummary = new { totalShares = 2 }
                });
            }

            if (request.RequestUri!.AbsoluteUri.StartsWith("https://api.linkedin.com/rest/organizationalEntityShareStatistics", StringComparison.Ordinal))
            {
                return JsonResponse(new
                {
                    elements = new object[]
                    {
                        new
                        {
                            totalShareStatistics = new
                            {
                                impressionCount = 1000,
                                uniqueImpressionsCount = 800,
                                clickCount = 33,
                                engagement = 17
                            }
                        }
                    }
                });
            }

            throw new InvalidOperationException($"Unexpected request: {request.RequestUri}");
        });

        var connection = harness.SeedConnection("r_organization_social,rw_organization_admin");
        harness.DbContext.PublishedLinkedInPosts.Add(new PublishedLinkedInPost
        {
            ConnectionId = connection.Id,
            ExternalPostId = "98765",
            PostUrn = "urn:li:ugcPost:98765",
            AuthorType = "organization",
            AuthorUrn = "urn:li:organization:1",
            OrganizationUrn = "urn:li:organization:1",
            Text = "Analytics",
            UpstreamMode = "rest_posts"
        });
        await harness.DbContext.SaveChangesAsync();

        var analytics = await harness.ConnectionService.GetPostAnalyticsAsync("98765", CancellationToken.None);

        Assert.Equal("ok", analytics.status);
        Assert.Equal(12, analytics.metrics["likes"]);
        Assert.Equal(1000, analytics.metrics["impressions"]);
        Assert.Equal("organization", analytics.authorType);
    }

    [Fact]
    public async Task LinkedInApiClient_UsesRestHeadersAndQueryTunneling()
    {
        using var harness = CreateHarness((_, _) => JsonResponse(new { elements = Array.Empty<object>() }));
        var longPostUrn = $"urn:li:ugcPost:{new string('9', 2200)}";

        await harness.ApiClient.GetOrganizationShareStatisticsAsync(
            "access-token",
            "urn:li:organization:1",
            longPostUrn,
            CancellationToken.None);

        var request = Assert.Single(harness.Handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("GET", request.Headers["X-HTTP-Method-Override"]);
        Assert.Equal("202602", request.Headers["Linkedin-Version"]);
        Assert.Equal("2.0.0", request.Headers["X-Restli-Protocol-Version"]);
        Assert.Equal("application/x-www-form-urlencoded", request.ContentType);
    }

    [Fact]
    public async Task LinkedInApiClient_NormalizesDeprecatedVersionErrors()
    {
        using var harness = CreateHarness((_, _) => JsonResponse(new
        {
            message = "The requested version is deprecated.",
            status = 426
        }, (HttpStatusCode)426));

        var exception = await Assert.ThrowsAsync<LinkedInApiException>(() =>
            harness.ApiClient.GetOrganizationAsync("access-token", "urn:li:organization:1", CancellationToken.None));

        Assert.Equal("UPSTREAM_VERSION_DEPRECATED", exception.Error.code);
        Assert.Equal(426, exception.Error.upstreamStatus);
    }

    private static Harness CreateHarness(Func<HttpRequestMessage, string?, HttpResponseMessage> responder)
    {
        var options = Options.Create(new LinkedInOptions
        {
            ClientId = "client-id",
            ClientSecret = "client-secret",
            RedirectUri = "https://localhost:7443/auth/linkedin/callback",
            DefaultApiVersion = "202602",
            Features = new LinkedInFeatureFlags
            {
                UserInfo = true,
                MemberPosting = true,
                OrganizationPosting = true,
                PostAnalytics = true,
                Webhooks = true
            }
        });

        var handler = new RecordingHttpMessageHandler(responder);
        var factory = new StubHttpClientFactory(handler);
        var dbContext = CreateDbContext();
        var dataProtection = new EphemeralDataProtectionProvider();
        var capabilityService = new LinkedInCapabilityService(options);
        var apiClient = new LinkedInApiClient(factory, options, NullLogger<LinkedInApiClient>.Instance);
        var tokenService = new LinkedInTokenService(factory, apiClient, options);
        var backgroundJobs = new LinkedInBackgroundJobService(dbContext, TimeProvider.System, NullLogger<LinkedInBackgroundJobService>.Instance);
        var organizationSyncService = new LinkedInOrganizationSyncService(dbContext, apiClient, TimeProvider.System);
        var postService = new LinkedInPostService(dbContext, apiClient, TimeProvider.System);
        var analyticsService = new LinkedInAnalyticsService(apiClient, TimeProvider.System);
        var connectionService = new LinkedInConnectionService(
            dbContext,
            dataProtection,
            capabilityService,
            backgroundJobs,
            tokenService,
            apiClient,
            organizationSyncService,
            postService,
            analyticsService,
            options,
            TimeProvider.System);

        return new Harness(dbContext, dataProtection, handler, apiClient, connectionService);
    }

    private static LinkedInMcpDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<LinkedInMcpDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("n"))
            .Options;

        return new LinkedInMcpDbContext(options);
    }

    private static HttpResponseMessage JsonResponse(object payload, HttpStatusCode statusCode = HttpStatusCode.OK)
        => new(statusCode)
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
        };

    private sealed class Harness(
        LinkedInMcpDbContext dbContext,
        IDataProtectionProvider dataProtection,
        RecordingHttpMessageHandler handler,
        LinkedInApiClient apiClient,
        LinkedInConnectionService connectionService) : IDisposable
    {
        public LinkedInMcpDbContext DbContext { get; } = dbContext;

        public RecordingHttpMessageHandler Handler { get; } = handler;

        public LinkedInApiClient ApiClient { get; } = apiClient;

        public LinkedInConnectionService ConnectionService { get; } = connectionService;

        public LinkedInConnection SeedConnection(string scopes)
        {
            var protector = dataProtection.CreateProtector("linkedin-tokens-v1");
            var connection = new LinkedInConnection
            {
                SubjectKey = "member-123",
                MemberUrn = "urn:li:person:member-123",
                DisplayName = "Ada Lovelace",
                Email = "ada@example.com",
                Status = "active",
                TokenStatus = "active",
                ScopeCsv = scopes,
                ValidatedScopeCsv = scopes,
                AccessTokenProtected = protector.Protect("access-token"),
                HasRefreshToken = false,
                AccessTokenExpiresAtUtc = DateTimeOffset.UtcNow.AddHours(1),
                UpdatedAtUtc = DateTimeOffset.UtcNow
            };

            DbContext.LinkedInConnections.Add(connection);
            DbContext.SaveChanges();
            return connection;
        }

        public void Dispose()
        {
            DbContext.Dispose();
            Handler.Dispose();
        }
    }

    private sealed class StubHttpClientFactory(RecordingHttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
            => new(handler, disposeHandler: false);
    }

    private sealed class RecordingHttpMessageHandler(Func<HttpRequestMessage, string?, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public List<CapturedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add(new CapturedRequest(
                request.Method,
                request.RequestUri!,
                request.Headers.ToDictionary(header => header.Key, header => string.Join(",", header.Value)),
                request.Content?.Headers.ContentType?.MediaType,
                body));

            var response = responder(request, body);
            if (response.RequestMessage is null)
            {
                response.RequestMessage = request;
            }

            return response;
        }
    }

    private sealed record CapturedRequest(
        HttpMethod Method,
        Uri Uri,
        IReadOnlyDictionary<string, string> Headers,
        string? ContentType,
        string? Body);
}
