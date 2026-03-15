using Aspire.Hosting;

EnsureDashboardEnvironment();

var builder = DistributedApplication.CreateBuilder(new DistributedApplicationOptions
{
    Args = args,
    AllowUnsecuredTransport = true
});

var postgres = builder.AddPostgres("postgres")
    .WithLifetime(ContainerLifetime.Persistent);

var database = postgres.AddDatabase("linkedinmcp");
var cache = builder.AddRedis("cache");
var linkedInClientId = builder.Configuration["LinkedIn:ClientId"];
var linkedInClientSecret = builder.Configuration["LinkedIn:ClientSecret"];
var linkedInRedirectUri = builder.Configuration["LinkedIn:RedirectUri"] ?? "https://localhost:7443/auth/linkedin/callback";

var server = builder.AddProject<Projects.LinkedInMcp_Server>("server")
    .WithReference(database)
    .WithReference(cache)
    .WaitFor(database)
    .WaitFor(cache)
    .WithEndpoint("http", e =>
    {
        e.Port = 5166;
        e.TargetPort = 5166;
        e.IsExternal = true;
        e.IsProxied = false;
    })
    .WithEndpoint("https", e =>
    {
        e.Port = 7443;
        e.TargetPort = 7443;
        e.IsExternal = true;
        e.IsProxied = false;
    });

var worker = builder.AddProject<Projects.LinkedInMcp_Worker>("worker")
    .WithReference(database)
    .WithReference(cache)
    .WaitFor(database)
    .WaitFor(cache);

server.WithEnvironment("LinkedIn__RedirectUri", linkedInRedirectUri);
worker.WithEnvironment("LinkedIn__RedirectUri", linkedInRedirectUri);

if (!string.IsNullOrWhiteSpace(linkedInClientId))
{
    server.WithEnvironment("LinkedIn__ClientId", linkedInClientId);
    worker.WithEnvironment("LinkedIn__ClientId", linkedInClientId);
}

if (!string.IsNullOrWhiteSpace(linkedInClientSecret))
{
    server.WithEnvironment("LinkedIn__ClientSecret", linkedInClientSecret);
    worker.WithEnvironment("LinkedIn__ClientSecret", linkedInClientSecret);
}

builder.Build().Run();

static void EnsureDashboardEnvironment()
{
    const string dashboardUrlVariable = "ASPNETCORE_URLS";
    const string otlpGrpcVariable = "ASPIRE_DASHBOARD_OTLP_ENDPOINT_URL";
    const string otlpHttpVariable = "ASPIRE_DASHBOARD_OTLP_HTTP_ENDPOINT_URL";

    if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(dashboardUrlVariable)))
    {
        Environment.SetEnvironmentVariable(dashboardUrlVariable, "http://127.0.0.1:18888");
    }

    if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(otlpGrpcVariable)))
    {
        Environment.SetEnvironmentVariable(otlpGrpcVariable, "http://127.0.0.1:18889");
    }

    if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(otlpHttpVariable)))
    {
        Environment.SetEnvironmentVariable(otlpHttpVariable, "http://127.0.0.1:18890");
    }
}
