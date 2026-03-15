using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using LinkedInMcp.Core.Configuration;
using LinkedInMcp.Core.Models;
using Microsoft.Extensions.Options;

namespace LinkedInMcp.Core.Services;

public sealed class LinkedInTokenService(
    IHttpClientFactory httpClientFactory,
    LinkedInApiClient apiClient,
    IOptions<LinkedInOptions> options)
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly LinkedInOptions _options = options.Value;

    public async Task<LinkedInTokenExchange> ExchangeAuthorizationCodeAsync(string code, CancellationToken cancellationToken)
    {
        using var client = httpClientFactory.CreateClient("linkedin-auth");
        using var response = await client.PostAsync(
            $"{_options.AuthBaseUrl.TrimEnd('/')}/oauth/v2/accessToken",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["code"] = code,
                ["client_id"] = _options.ClientId,
                ["client_secret"] = _options.ClientSecret,
                ["redirect_uri"] = _options.RedirectUri
            }),
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw await CreateTokenExceptionAsync(response, cancellationToken);
        }

        return await ReadExchangeAsync(response, cancellationToken);
    }

    public async Task<LinkedInTokenExchange> RefreshAccessTokenAsync(string refreshToken, CancellationToken cancellationToken)
    {
        using var client = httpClientFactory.CreateClient("linkedin-auth");
        using var response = await client.PostAsync(
            $"{_options.AuthBaseUrl.TrimEnd('/')}/oauth/v2/accessToken",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = refreshToken,
                ["client_id"] = _options.ClientId,
                ["client_secret"] = _options.ClientSecret
            }),
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw await CreateTokenExceptionAsync(response, cancellationToken);
        }

        return await ReadExchangeAsync(response, cancellationToken);
    }

    public Task<TokenIntrospectionDocument> IntrospectAsync(string accessToken, CancellationToken cancellationToken)
        => apiClient.IntrospectTokenAsync(accessToken, _options.ClientId, _options.ClientSecret, cancellationToken);

    private static async Task<LinkedInTokenExchange> ReadExchangeAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var payload = await response.Content.ReadFromJsonAsync<TokenExchangeResponse>(SerializerOptions, cancellationToken)
            ?? throw new InvalidOperationException("LinkedIn token exchange returned an empty payload.");

        return new LinkedInTokenExchange(
            payload.AccessToken,
            payload.ExpiresIn,
            payload.RefreshToken,
            payload.RefreshTokenExpiresIn);
    }

    private static async Task<LinkedInApiException> CreateTokenExceptionAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var message = await response.Content.ReadAsStringAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(message))
        {
            message = response.ReasonPhrase ?? "LinkedIn token request failed.";
        }

        return new LinkedInApiException(new LinkedInErrorDetails(
            response.StatusCode == System.Net.HttpStatusCode.Unauthorized ? "TOKEN_EXCHANGE_FAILED" : "TOKEN_REQUEST_FAILED",
            message,
            (int)response.StatusCode,
            response.Headers.TryGetValues("x-li-request-id", out var requestIds) ? requestIds.FirstOrDefault() : null,
            retryable: false));
    }

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
}

public sealed record LinkedInTokenExchange(
    string AccessToken,
    int ExpiresInSeconds,
    string? RefreshToken,
    int? RefreshTokenExpiresInSeconds);
