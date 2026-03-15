using LinkedInMcp.Core.Services;
using LinkedInMcp.ServiceDefaults;
using LinkedInMcp.Worker;

var builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddLinkedInMcpCore(builder.Configuration);
builder.Services.AddHostedService<Worker>();

var host = builder.Build();

await host.Services.InitializeLinkedInMcpDatabaseAsync();
await host.RunAsync();
