using Aspire.Hosting;

var builder = DistributedApplication.CreateBuilder(args);

var postgres = builder.AddPostgres("postgres")
    .WithLifetime(ContainerLifetime.Persistent);

var database = postgres.AddDatabase("linkedinmcp");
var cache = builder.AddRedis("cache");

builder.AddProject<Projects.LinkedInMcp_Server>("server")
    .WithReference(database)
    .WithReference(cache)
    .WaitFor(database)
    .WaitFor(cache)
    .WithExternalHttpEndpoints();

builder.AddProject<Projects.LinkedInMcp_Worker>("worker")
    .WithReference(database)
    .WithReference(cache)
    .WaitFor(database)
    .WaitFor(cache);

builder.Build().Run();
