using LinkedInMcp.Core.Services;
using LinkedInMcp.Server.Mcp;
using LinkedInMcp.ServiceDefaults;
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
    string state,
    string code,
    LinkedInConnectionService connectionService,
    CancellationToken cancellationToken) =>
{
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
