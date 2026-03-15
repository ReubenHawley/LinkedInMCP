using LinkedInMcp.Core.Services;
using LinkedInMcp.Server;
using LinkedInMcp.Server.Mcp;
using ModelContextProtocol.Server;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddLinkedInMcpCore(builder.Configuration);
builder.Services.AddMcpServer()
    .WithHttpTransport()
    .WithTools<LinkedInTools>()
    .WithResources<LinkedInResources>();

var app = builder.Build();

app.UseExceptionHandler();
app.UseHttpsRedirection();

app.MapGet("/", () => Results.Ok(new
{
    name = "LinkedIn MCP Server",
    mcp = "/mcp",
    callback = "/auth/linkedin/callback",
    webhook = "/webhooks/linkedin"
}));

app.MapGet("/auth/linkedin/callback", async (
    HttpRequest request,
    LinkedInConnectionService connectionService,
    CancellationToken cancellationToken) =>
{
    var oauthError = request.Query["error"].ToString();
    var oauthErrorDescription = request.Query["error_description"].ToString();
    var state = request.Query["state"].ToString();
    var code = request.Query["code"].ToString();

    if (!string.IsNullOrWhiteSpace(oauthError))
    {
        return Results.BadRequest(new
        {
            error = oauthError,
            errorDescription = oauthErrorDescription,
            callback = "/auth/linkedin/callback",
            troubleshooting = new[]
            {
                "Verify the exact redirect URI in the LinkedIn app Auth tab matches https://localhost:7443/auth/linkedin/callback.",
                "Verify the LinkedIn app is approved for every requested scope in the Products tab.",
                "Verify the local server is reachable over HTTPS before starting the OAuth flow."
            }
        });
    }

    if (string.IsNullOrWhiteSpace(state) || string.IsNullOrWhiteSpace(code))
    {
        return Results.BadRequest(new
        {
            error = "missing_oauth_parameters",
            callback = "/auth/linkedin/callback",
            message = "LinkedIn did not return both state and code query parameters."
        });
    }

    var result = await connectionService.CompleteOAuthCallbackAsync(state, code, cancellationToken);
    return Results.Ok(result);
});

app.MapGet("/webhooks/linkedin", (string challengeCode, LinkedInWebhookService webhookService) =>
{
    var challengeResponse = webhookService.CreateChallengeResponse(challengeCode);
    return Results.Ok(new
    {
        challengeCode,
        challengeResponse
    });
});

app.MapPost("/webhooks/linkedin", async (
    HttpRequest request,
    LinkedInWebhookService webhookService,
    CancellationToken cancellationToken) =>
{
    using var reader = new StreamReader(request.Body);
    var body = await reader.ReadToEndAsync(cancellationToken);
    var receipt = await webhookService.RecordDeliveryAsync(
        body,
        request.Headers["X-LI-Signature"],
        cancellationToken);

    return receipt.status == "invalid-signature"
        ? Results.Unauthorized()
        : Results.Accepted($"/webhooks/linkedin/{receipt.deliveryId}", receipt);
});

app.MapMcp("/mcp");
app.MapDefaultEndpoints();

await app.Services.InitializeLinkedInMcpDatabaseAsync();
await app.RunAsync();

public partial class Program;
