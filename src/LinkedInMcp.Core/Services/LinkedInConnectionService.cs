using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
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
    IHttpClientFactory httpClientFactory,
    IDataProtectionProvider dataProtectionProvider,
    LinkedInCapabilityService capabilityService,
    LinkedInBackgroundJobService backgroundJobs,
    IOptions<LinkedInOptions> options,
    TimeProvider timeProvider)
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
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
            "https://www.linkedin.com/oauth/v2/authorization",
            new Dictionary<string, string?>
            {
                ["response_type"] = "code",
                ["client_id"] = _options.ClientId,
                ["redirect_uri"] = _options.RedirectUri,
                ["state"] = state,
                ["scope"] = string.Join(' ', scopes)
            });

        return new AuthUrlResponse(authorizationUrl, state, scopes, expiresAt);
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

        var tokenResponse = await ExchangeAuthorizationCodeAsync(code, cancellationToken);
        var userInfo = await GetUserInfoAsync(tokenResponse.AccessToken, cancellationToken);

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
        connection.Status = "active";
        connection.ScopeCsv = authState.RequestedScopeCsv;
        connection.HasRefreshToken = !string.IsNullOrWhiteSpace(tokenResponse.RefreshToken);
        connection.AccessTokenProtected = _protector.Protect(tokenResponse.AccessToken);
        connection.RefreshTokenProtected = string.IsNullOrWhiteSpace(tokenResponse.RefreshToken)
            ? null
            : _protector.Protect(tokenResponse.RefreshToken);
        connection.AccessTokenExpiresAtUtc = timeProvider.GetUtcNow().AddSeconds(tokenResponse.ExpiresIn);
        connection.RefreshTokenExpiresAtUtc = tokenResponse.RefreshTokenExpiresIn is int refreshExpiresIn
            ? timeProvider.GetUtcNow().AddSeconds(refreshExpiresIn)
            : null;
        connection.UpdatedAtUtc = timeProvider.GetUtcNow();

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
            var accessToken = UnprotectRequiredToken(connection.AccessTokenProtected);
            var userInfo = await GetUserInfoAsync(accessToken, cancellationToken);
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
            connection.ScopeCsv.ToList(),
            connection.UpdatedAtUtc);
    }

    public async Task<IReadOnlyList<OrganizationAccessSnapshot>> ListOrganizationsAsync(Guid connectionId, CancellationToken cancellationToken)
    {
        return await dbContext.LinkedInOrganizationAccess
            .Where(access => access.ConnectionId == connectionId)
            .OrderBy(access => access.DisplayName)
            .Select(access => new OrganizationAccessSnapshot(
                access.ConnectionId,
                access.OrganizationUrn,
                access.DisplayName,
                access.RolesCsv.ToList(),
                access.LastSyncedAtUtc))
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
            organization.LastSyncedAtUtc);
    }

    public async Task<ConnectionStatusResponse> RefreshAsync(Guid connectionId, CancellationToken cancellationToken)
    {
        EnsureClientConfiguration();

        var connection = await ResolveRequiredConnectionAsync(connectionId, cancellationToken);
        if (!connection.HasRefreshToken || string.IsNullOrWhiteSpace(connection.RefreshTokenProtected))
        {
            throw new InvalidOperationException("This LinkedIn connection does not have a refresh token.");
        }

        var refreshToken = UnprotectRequiredToken(connection.RefreshTokenProtected);
        var response = await RefreshAccessTokenAsync(refreshToken, cancellationToken);

        connection.AccessTokenProtected = _protector.Protect(response.AccessToken);
        connection.AccessTokenExpiresAtUtc = timeProvider.GetUtcNow().AddSeconds(response.ExpiresIn);
        if (!string.IsNullOrWhiteSpace(response.RefreshToken))
        {
            connection.RefreshTokenProtected = _protector.Protect(response.RefreshToken);
        }

        if (response.RefreshTokenExpiresIn is int refreshExpiresIn)
        {
            connection.RefreshTokenExpiresAtUtc = timeProvider.GetUtcNow().AddSeconds(refreshExpiresIn);
        }

        connection.UpdatedAtUtc = timeProvider.GetUtcNow();

        await dbContext.SaveChangesAsync(cancellationToken);

        return await GetConnectionStatusAsync(connectionId, cancellationToken);
    }

    public async Task<DeletionResult> DeleteStoredDataAsync(Guid connectionId, CancellationToken cancellationToken)
    {
        var connection = await ResolveRequiredConnectionAsync(connectionId, cancellationToken);

        connection.Status = "deleted";
        connection.AccessTokenProtected = null;
        connection.RefreshTokenProtected = null;
        connection.HasRefreshToken = false;
        connection.UpdatedAtUtc = timeProvider.GetUtcNow();

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

    public OperationResult CreateUnsupportedResult(string featureName)
        => new(
            "not_implemented",
            $"{featureName} is represented in the MCP contract, but the live LinkedIn API workflow still requires product-approved payload shaping before it can be executed safely.");

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

    private async Task<TokenExchangeResponse> ExchangeAuthorizationCodeAsync(string code, CancellationToken cancellationToken)
    {
        using var client = httpClientFactory.CreateClient("linkedin-auth");
        using var response = await client.PostAsync(
            "https://www.linkedin.com/oauth/v2/accessToken",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["code"] = code,
                ["client_id"] = _options.ClientId,
                ["client_secret"] = _options.ClientSecret,
                ["redirect_uri"] = _options.RedirectUri
            }),
            cancellationToken);

        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<TokenExchangeResponse>(SerializerOptions, cancellationToken)
            ?? throw new InvalidOperationException("LinkedIn token exchange returned an empty payload.");

        return payload;
    }

    private async Task<TokenExchangeResponse> RefreshAccessTokenAsync(string refreshToken, CancellationToken cancellationToken)
    {
        using var client = httpClientFactory.CreateClient("linkedin-auth");
        using var response = await client.PostAsync(
            "https://www.linkedin.com/oauth/v2/accessToken",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = refreshToken,
                ["client_id"] = _options.ClientId,
                ["client_secret"] = _options.ClientSecret
            }),
            cancellationToken);

        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<TokenExchangeResponse>(SerializerOptions, cancellationToken)
            ?? throw new InvalidOperationException("LinkedIn token refresh returned an empty payload.");

        return payload;
    }

    private async Task<UserInfoResponse> GetUserInfoAsync(string accessToken, CancellationToken cancellationToken)
    {
        using var client = httpClientFactory.CreateClient("linkedin-api");
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.linkedin.com/v2/userinfo");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.TryAddWithoutValidation("X-Restli-Protocol-Version", "2.0.0");

        using var response = await client.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<UserInfoResponse>(SerializerOptions, cancellationToken)
            ?? throw new InvalidOperationException("LinkedIn user info returned an empty payload.");

        return payload;
    }

    private void EnsureClientConfiguration()
    {
        if (string.IsNullOrWhiteSpace(_options.ClientId) ||
            string.IsNullOrWhiteSpace(_options.ClientSecret) ||
            string.IsNullOrWhiteSpace(_options.RedirectUri))
        {
            throw new InvalidOperationException(
                "LinkedIn OAuth is not fully configured. Set LinkedIn:ClientId, LinkedIn:ClientSecret, and LinkedIn:RedirectUri.");
        }
    }

    private string UnprotectRequiredToken(string? protectedToken)
        => string.IsNullOrWhiteSpace(protectedToken)
            ? throw new InvalidOperationException("LinkedIn token data is missing.")
            : _protector.Unprotect(protectedToken);

    private sealed class TokenExchangeResponse
    {
        [JsonPropertyName("access_token")]
        public string AccessToken { get; set; } = string.Empty;

        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; set; }

        [JsonPropertyName("refresh_token")]
        public string? RefreshToken { get; set; }

        [JsonPropertyName("refresh_token_expires_in")]
        public int? RefreshTokenExpiresIn { get; set; }
    }

    private sealed class UserInfoResponse
    {
        [JsonPropertyName("sub")]
        public string Subject { get; set; } = string.Empty;

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("email")]
        public string? Email { get; set; }
    }
}
