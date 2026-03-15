using System.Security.Cryptography;
using LinkedInMcp.Core.Configuration;
using LinkedInMcp.Core.Data;
using LinkedInMcp.Core.Models;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace LinkedInMcp.Core.Services;

public sealed class LinkedInConnectionService(
    LinkedInMcpDbContext dbContext,
    IDataProtectionProvider dataProtectionProvider,
    LinkedInCapabilityService capabilityService,
    LinkedInBackgroundJobService backgroundJobs,
    LinkedInTokenService tokenService,
    LinkedInApiClient apiClient,
    LinkedInOrganizationSyncService organizationSyncService,
    LinkedInPostService postService,
    LinkedInAnalyticsService analyticsService,
    IOptions<LinkedInOptions> options,
    TimeProvider timeProvider)
{
    private readonly IDataProtector _protector = dataProtectionProvider.CreateProtector("linkedin-tokens-v1");
    private readonly LinkedInOptions _options = options.Value;

    public async Task<AuthUrlResponse> BeginAuthAsync(IEnumerable<string>? requestedScopes, string? returnUrl, CancellationToken cancellationToken)
    {
        EnsureClientConfiguration();

        var scopes = (requestedScopes ?? _options.DefaultScopes)
            .DefaultIfEmpty("openid")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var state = Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();
        var expiresAt = timeProvider.GetUtcNow().AddMinutes(30);

        dbContext.LinkedInOAuthStates.Add(new LinkedInOAuthState
        {
            State = state,
            RequestedScopeCsv = scopes.ToCsv(),
            ReturnUrl = returnUrl,
            ExpiresAtUtc = expiresAt
        });

        await dbContext.SaveChangesAsync(cancellationToken);

        var authorizationUrl = QueryHelpers.AddQueryString(
            $"{_options.AuthBaseUrl.TrimEnd('/')}/oauth/v2/authorization",
            new Dictionary<string, string?>
            {
                ["response_type"] = "code",
                ["client_id"] = _options.ClientId,
                ["redirect_uri"] = _options.RedirectUri,
                ["state"] = state,
                ["scope"] = string.Join(' ', scopes)
            });

        return new AuthUrlResponse(
            authorizationUrl,
            authorizationUrl,
            "Open browserReadyAuthorizationUrl directly in a browser. Copy only the URL value, not the surrounding JSON response.",
            _options.RedirectUri,
            "Configure the exact redirectUri in the LinkedIn app Auth tab, and make sure the app is approved for every requested scope before starting the browser flow.",
            state,
            scopes,
            expiresAt);
    }

    public async Task<ConnectionStatusResponse> CompleteOAuthCallbackAsync(string state, string code, CancellationToken cancellationToken)
    {
        EnsureClientConfiguration();

        var authState = await dbContext.LinkedInOAuthStates
            .SingleOrDefaultAsync(existing => existing.State == state, cancellationToken)
            ?? throw new InvalidOperationException("OAuth state was not found.");

        if (authState.CompletedAtUtc is not null || authState.ExpiresAtUtc < timeProvider.GetUtcNow())
        {
            throw new InvalidOperationException("OAuth state is no longer valid.");
        }

        var tokenResponse = await tokenService.ExchangeAuthorizationCodeAsync(code, cancellationToken);
        var introspection = await tokenService.IntrospectAsync(tokenResponse.AccessToken, cancellationToken);
        var userInfo = await apiClient.GetUserInfoAsync(tokenResponse.AccessToken, cancellationToken);

        var connection = await dbContext.LinkedInConnections
            .SingleOrDefaultAsync(existing => existing.SubjectKey == userInfo.Subject, cancellationToken);

        if (connection is null)
        {
            connection = new LinkedInConnection
            {
                SubjectKey = userInfo.Subject,
                CreatedAtUtc = timeProvider.GetUtcNow()
            };

            dbContext.LinkedInConnections.Add(connection);
        }

        connection.DisplayName = userInfo.Name ?? userInfo.Subject;
        connection.Email = userInfo.Email;
        connection.MemberUrn = $"urn:li:person:{userInfo.Subject}";

        ApplyTokenState(connection, tokenResponse, introspection, authState.RequestedScopeCsv.ToList());

        authState.CompletedAtUtc = timeProvider.GetUtcNow();

        await dbContext.SaveChangesAsync(cancellationToken);

        return await GetConnectionStatusAsync(connection.Id, cancellationToken);
    }

    public async Task<ConnectionStatusResponse> GetConnectionStatusAsync(Guid? connectionId, CancellationToken cancellationToken)
    {
        var connection = await ResolveConnectionAsync(connectionId, cancellationToken);
        var capabilities = capabilityService.BuildSnapshot(connection);

        return new ConnectionStatusResponse(
            connection?.Id,
            connection?.Status ?? "disconnected",
            connection?.SubjectKey,
            connection?.DisplayName,
            connection?.Email,
            capabilities);
    }

    public async Task<CapabilitySnapshot> GetCapabilitiesAsync(Guid? connectionId, CancellationToken cancellationToken)
    {
        var connection = await ResolveConnectionAsync(connectionId, cancellationToken);
        return capabilityService.BuildSnapshot(connection);
    }

    public async Task<LinkedInProfileSnapshot> GetProfileAsync(Guid? connectionId, bool refreshFromLinkedIn, CancellationToken cancellationToken)
    {
        var connection = await ResolveRequiredConnectionAsync(connectionId, cancellationToken);

        if (refreshFromLinkedIn)
        {
            var userInfo = await ExecuteAuthorizedAsync(
                connection,
                (accessToken, ct) => apiClient.GetUserInfoAsync(accessToken, ct),
                cancellationToken);

            connection.DisplayName = userInfo.Name ?? connection.DisplayName;
            connection.Email = userInfo.Email ?? connection.Email;
            connection.UpdatedAtUtc = timeProvider.GetUtcNow();
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return new LinkedInProfileSnapshot(
            connection.Id,
            connection.SubjectKey,
            connection.DisplayName,
            connection.Email,
            connection.GetEffectiveScopes(),
            connection.UpdatedAtUtc);
    }

    public async Task<IReadOnlyList<OrganizationAccessSnapshot>> ListOrganizationsAsync(Guid connectionId, CancellationToken cancellationToken)
    {
        await ResolveRequiredConnectionAsync(connectionId, cancellationToken);

        return await dbContext.LinkedInOrganizationAccess
            .Where(access => access.ConnectionId == connectionId)
            .OrderBy(access => access.DisplayName)
            .Select(access => new OrganizationAccessSnapshot(
                access.ConnectionId,
                access.OrganizationUrn,
                access.DisplayName,
                access.RolesCsv.ToList(),
                access.LastSyncedAtUtc,
                access.SyncStatus))
            .ToListAsync(cancellationToken);
    }

    public async Task<OrganizationAccessSnapshot> GetOrganizationAsync(Guid connectionId, string organizationUrn, CancellationToken cancellationToken)
    {
        var organization = await dbContext.LinkedInOrganizationAccess
            .SingleOrDefaultAsync(
                access => access.ConnectionId == connectionId && access.OrganizationUrn == organizationUrn,
                cancellationToken)
            ?? throw new InvalidOperationException($"Organization '{organizationUrn}' was not found.");

        return new OrganizationAccessSnapshot(
            organization.ConnectionId,
            organization.OrganizationUrn,
            organization.DisplayName,
            organization.RolesCsv.ToList(),
            organization.LastSyncedAtUtc,
            organization.SyncStatus);
    }

    public async Task<OrganizationSyncResult> SyncOrganizationsAsync(Guid? connectionId, CancellationToken cancellationToken)
    {
        var connection = await ResolveRequiredConnectionAsync(connectionId, cancellationToken);
        try
        {
            return await ExecuteAuthorizedAsync(
                connection,
                (accessToken, ct) => organizationSyncService.SyncOrganizationsAsync(connection, accessToken, ct),
                cancellationToken);
        }
        catch (LinkedInApiException exception)
        {
            return new OrganizationSyncResult(
                "error",
                connection.Id,
                0,
                0,
                timeProvider.GetUtcNow(),
                exception.Error);
        }
    }

    public async Task<PostPublishResult> CreateMemberPostAsync(Guid connectionId, string text, CancellationToken cancellationToken)
    {
        var connection = await ResolveRequiredConnectionAsync(connectionId, cancellationToken);
        if (!capabilityService.BuildSnapshot(connection).features.publish_member_post)
        {
            return CreatePostError(connection.Id, "member", connection.GetMemberUrn(), null, "MEMBER_POSTING_NOT_ALLOWED", "The connection is not approved to publish member posts.");
        }

        try
        {
            return await ExecuteAuthorizedAsync(
                connection,
                (accessToken, ct) => postService.CreateMemberPostAsync(connection, accessToken, text, ct),
                cancellationToken);
        }
        catch (LinkedInApiException exception)
        {
            return CreatePostError(connection.Id, "member", connection.GetMemberUrn(), null, exception.Error);
        }
    }

    public async Task<PostPublishResult> CreateOrganizationPostAsync(Guid connectionId, string organizationUrn, string text, CancellationToken cancellationToken)
    {
        var connection = await ResolveRequiredConnectionAsync(connectionId, cancellationToken);
        if (!capabilityService.BuildSnapshot(connection).features.publish_org_post)
        {
            return CreatePostError(connection.Id, "organization", organizationUrn, organizationUrn, "ORG_POSTING_NOT_ALLOWED", "The connection is not approved to publish organization posts.");
        }

        try
        {
            return await ExecuteAuthorizedAsync(
                connection,
                (accessToken, ct) => postService.CreateOrganizationPostAsync(connection, accessToken, organizationUrn, text, ct),
                cancellationToken);
        }
        catch (LinkedInApiException exception)
        {
            return CreatePostError(connection.Id, "organization", organizationUrn, organizationUrn, exception.Error);
        }
    }

    public async Task<PostAnalyticsSnapshot> GetPostAnalyticsAsync(string postIdOrUrn, CancellationToken cancellationToken)
    {
        var postRecord = await dbContext.PublishedLinkedInPosts
            .OrderByDescending(post => post.PublishedAtUtc)
            .FirstOrDefaultAsync(
                post => post.ExternalPostId == postIdOrUrn || post.PostUrn == postIdOrUrn,
                cancellationToken);

        var connection = postRecord is null
            ? await ResolveRequiredConnectionAsync(connectionId: null, cancellationToken)
            : await ResolveRequiredConnectionAsync(postRecord.ConnectionId, cancellationToken);

        if (!capabilityService.BuildSnapshot(connection).features.post_analytics)
        {
            return new PostAnalyticsSnapshot(
                "error",
                connection.Id,
                postIdOrUrn,
                postRecord?.PostUrn,
                postRecord?.AuthorType ?? "unknown",
                postRecord?.AuthorUrn ?? connection.GetMemberUrn(),
                postRecord?.OrganizationUrn,
                new Dictionary<string, long>(),
                timeProvider.GetUtcNow(),
                new LinkedInErrorDetails(
                    "POST_ANALYTICS_NOT_ALLOWED",
                    "The connection does not have the scopes or feature flags required for post analytics.",
                    null,
                    null,
                    false));
        }

        try
        {
            return await ExecuteAuthorizedAsync(
                connection,
                (accessToken, ct) => analyticsService.GetAnalyticsAsync(connection, accessToken, postRecord, postIdOrUrn, ct),
                cancellationToken);
        }
        catch (LinkedInApiException exception)
        {
            return new PostAnalyticsSnapshot(
                "error",
                connection.Id,
                postRecord?.ExternalPostId ?? postIdOrUrn,
                postRecord?.PostUrn,
                postRecord?.AuthorType ?? "unknown",
                postRecord?.AuthorUrn ?? connection.GetMemberUrn(),
                postRecord?.OrganizationUrn,
                new Dictionary<string, long>(),
                timeProvider.GetUtcNow(),
                exception.Error);
        }
    }

    public async Task<ConnectionStatusResponse> RefreshAsync(Guid connectionId, CancellationToken cancellationToken)
    {
        EnsureClientConfiguration();

        var connection = await ResolveRequiredConnectionAsync(connectionId, cancellationToken);
        await RefreshConnectionCoreAsync(connection, cancellationToken);
        return await GetConnectionStatusAsync(connectionId, cancellationToken);
    }

    public async Task<DeletionResult> DeleteStoredDataAsync(Guid connectionId, CancellationToken cancellationToken)
    {
        var connection = await ResolveRequiredConnectionAsync(connectionId, cancellationToken);

        connection.Status = "deleted";
        connection.TokenStatus = "deleted";
        connection.AccessTokenProtected = null;
        connection.RefreshTokenProtected = null;
        connection.HasRefreshToken = false;
        connection.ValidatedScopeCsv = string.Empty;
        connection.UpdatedAtUtc = timeProvider.GetUtcNow();

        var organizationAccess = await dbContext.LinkedInOrganizationAccess
            .Where(access => access.ConnectionId == connectionId)
            .ToArrayAsync(cancellationToken);
        var posts = await dbContext.PublishedLinkedInPosts
            .Where(post => post.ConnectionId == connectionId)
            .ToArrayAsync(cancellationToken);

        if (organizationAccess.Length > 0)
        {
            dbContext.LinkedInOrganizationAccess.RemoveRange(organizationAccess);
        }

        if (posts.Length > 0)
        {
            dbContext.PublishedLinkedInPosts.RemoveRange(posts);
        }

        var deletionRequest = new DeletionRequest
        {
            ConnectionId = connectionId,
            SubjectKey = connection.SubjectKey,
            RequestedAtUtc = timeProvider.GetUtcNow()
        };

        dbContext.DeletionRequests.Add(deletionRequest);
        await dbContext.SaveChangesAsync(cancellationToken);

        await backgroundJobs.EnqueueAsync(
            "deletion-request",
            new LinkedInBackgroundJobService.DeletionJobPayload(deletionRequest.Id),
            cancellationToken);

        return new DeletionResult(connectionId, deletionRequest.Status, deletionRequest.RequestedAtUtc);
    }

    private async Task<T> ExecuteAuthorizedAsync<T>(
        LinkedInConnection connection,
        Func<string, CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken)
    {
        var accessToken = await GetUsableAccessTokenAsync(connection, cancellationToken);

        try
        {
            return await operation(accessToken, cancellationToken);
        }
        catch (LinkedInApiException exception) when (exception.Error.code == "TOKEN_EXPIRED" && connection.HasRefreshToken)
        {
            accessToken = await RefreshConnectionCoreAsync(connection, cancellationToken);
            return await operation(accessToken, cancellationToken);
        }
        catch (LinkedInApiException exception) when (exception.Error.code == "TOKEN_REVOKED")
        {
            await MarkConnectionRevokedAsync(connection, cancellationToken);
            throw;
        }
    }

    private async Task<string> GetUsableAccessTokenAsync(LinkedInConnection connection, CancellationToken cancellationToken)
    {
        if (connection.AccessTokenExpiresAtUtc is not null &&
            connection.AccessTokenExpiresAtUtc <= timeProvider.GetUtcNow().AddMinutes(2) &&
            connection.HasRefreshToken)
        {
            return await RefreshConnectionCoreAsync(connection, cancellationToken);
        }

        return UnprotectRequiredToken(connection.AccessTokenProtected);
    }

    private async Task<string> RefreshConnectionCoreAsync(LinkedInConnection connection, CancellationToken cancellationToken)
    {
        if (!connection.HasRefreshToken || string.IsNullOrWhiteSpace(connection.RefreshTokenProtected))
        {
            throw new InvalidOperationException("This LinkedIn connection does not have a refresh token.");
        }

        var refreshToken = UnprotectRequiredToken(connection.RefreshTokenProtected);
        var response = await tokenService.RefreshAccessTokenAsync(refreshToken, cancellationToken);
        var introspection = await tokenService.IntrospectAsync(response.AccessToken, cancellationToken);

        ApplyTokenState(connection, response, introspection, connection.ScopeCsv.ToList());
        connection.UpdatedAtUtc = timeProvider.GetUtcNow();

        await dbContext.SaveChangesAsync(cancellationToken);
        return response.AccessToken;
    }

    private void ApplyTokenState(
        LinkedInConnection connection,
        LinkedInTokenExchange tokenResponse,
        TokenIntrospectionDocument introspection,
        IReadOnlyList<string> fallbackScopes)
    {
        var now = timeProvider.GetUtcNow();

        connection.Status = introspection.active ? "active" : "revoked";
        connection.AuthType = string.IsNullOrWhiteSpace(introspection.authType) ? "oidc" : introspection.authType;
        connection.ScopeCsv = (introspection.scopes.Count > 0 ? introspection.scopes : fallbackScopes).ToCsv();
        connection.ValidatedScopeCsv = introspection.scopes.ToCsv();
        connection.TokenStatus = introspection.active ? "active" : "revoked";
        connection.HasRefreshToken = !string.IsNullOrWhiteSpace(tokenResponse.RefreshToken) || connection.HasRefreshToken;
        connection.AccessTokenProtected = _protector.Protect(tokenResponse.AccessToken);
        connection.AccessTokenExpiresAtUtc = introspection.expiresAtUtc ?? now.AddSeconds(tokenResponse.ExpiresInSeconds);
        connection.LastValidatedAtUtc = now;
        connection.UpdatedAtUtc = now;

        if (!string.IsNullOrWhiteSpace(tokenResponse.RefreshToken))
        {
            connection.RefreshTokenProtected = _protector.Protect(tokenResponse.RefreshToken);
        }

        if (tokenResponse.RefreshTokenExpiresInSeconds is int refreshExpiresIn)
        {
            connection.RefreshTokenExpiresAtUtc = now.AddSeconds(refreshExpiresIn);
        }
    }

    private async Task MarkConnectionRevokedAsync(LinkedInConnection connection, CancellationToken cancellationToken)
    {
        connection.Status = "revoked";
        connection.TokenStatus = "revoked";
        connection.LastValidatedAtUtc = timeProvider.GetUtcNow();
        connection.UpdatedAtUtc = timeProvider.GetUtcNow();
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<LinkedInConnection?> ResolveConnectionAsync(Guid? connectionId, CancellationToken cancellationToken)
    {
        if (connectionId is Guid explicitId)
        {
            return await dbContext.LinkedInConnections.SingleOrDefaultAsync(connection => connection.Id == explicitId, cancellationToken);
        }

        return await dbContext.LinkedInConnections
            .OrderByDescending(connection => connection.UpdatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private async Task<LinkedInConnection> ResolveRequiredConnectionAsync(Guid? connectionId, CancellationToken cancellationToken)
        => await ResolveConnectionAsync(connectionId, cancellationToken)
            ?? throw new InvalidOperationException("No LinkedIn connection is available.");

    private void EnsureClientConfiguration()
    {
        if (string.IsNullOrWhiteSpace(_options.ClientId) ||
            string.IsNullOrWhiteSpace(_options.ClientSecret) ||
            string.IsNullOrWhiteSpace(_options.RedirectUri))
        {
            throw new InvalidOperationException(
                "LinkedIn OAuth is not fully configured. Set LinkedIn:ClientId, LinkedIn:ClientSecret, and LinkedIn:RedirectUri. " +
                "For local development, add them with `dotnet user-secrets --project src/LinkedInMcp.AppHost set \"LinkedIn:ClientId\" \"...\"`, " +
                "`dotnet user-secrets --project src/LinkedInMcp.AppHost set \"LinkedIn:ClientSecret\" \"...\"`, and " +
                "`dotnet user-secrets --project src/LinkedInMcp.AppHost set \"LinkedIn:RedirectUri\" \"https://localhost:7443/auth/linkedin/callback\"`, " +
                "or provide the equivalent LinkedIn__* environment variables when running the server directly.");
        }
    }

    private string UnprotectRequiredToken(string? protectedToken)
        => string.IsNullOrWhiteSpace(protectedToken)
            ? throw new InvalidOperationException("LinkedIn token data is missing.")
            : _protector.Unprotect(protectedToken);

    private static PostPublishResult CreatePostError(
        Guid connectionId,
        string authorType,
        string authorUrn,
        string? organizationUrn,
        string code,
        string message)
        => CreatePostError(
            connectionId,
            authorType,
            authorUrn,
            organizationUrn,
            new LinkedInErrorDetails(code, message, null, null, false));

    private static PostPublishResult CreatePostError(
        Guid connectionId,
        string authorType,
        string authorUrn,
        string? organizationUrn,
        LinkedInErrorDetails error)
        => new(
            "error",
            connectionId,
            null,
            null,
            authorType,
            authorUrn,
            organizationUrn,
            publishedAtUtc: null,
            upstreamMode: null,
            error);
}
