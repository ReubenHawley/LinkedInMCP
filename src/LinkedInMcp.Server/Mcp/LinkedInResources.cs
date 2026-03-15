using System.ComponentModel;
using System.Text.Json;
using LinkedInMcp.Core.Services;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace LinkedInMcp.Server.Mcp;

[McpServerResourceType]
public sealed class LinkedInResources(LinkedInConnectionService connectionService)
{
    [McpServerResource(
        UriTemplate = "linkedin://connections/{connectionId}/capabilities",
        Name = "LinkedIn Connection Capabilities",
        MimeType = "application/json")]
    [Description("Returns the capability snapshot for a LinkedIn connection.")]
    public async Task<TextResourceContents> GetConnectionCapabilities(string connectionId, CancellationToken cancellationToken)
        => JsonResource(
            $"linkedin://connections/{connectionId}/capabilities",
            await connectionService.GetCapabilitiesAsync(Guid.Parse(connectionId), cancellationToken));

    [McpServerResource(
        UriTemplate = "linkedin://connections/{connectionId}/profile",
        Name = "LinkedIn Connection Profile",
        MimeType = "application/json")]
    [Description("Returns the cached LinkedIn profile for a connection.")]
    public async Task<TextResourceContents> GetConnectionProfile(string connectionId, CancellationToken cancellationToken)
        => JsonResource(
            $"linkedin://connections/{connectionId}/profile",
            await connectionService.GetProfileAsync(Guid.Parse(connectionId), refreshFromLinkedIn: false, cancellationToken));

    [McpServerResource(
        UriTemplate = "linkedin://organizations/{connectionId}/{organizationUrn}",
        Name = "LinkedIn Organization Snapshot",
        MimeType = "application/json")]
    [Description("Returns one cached LinkedIn organization record for a connection.")]
    public async Task<TextResourceContents> GetOrganization(string connectionId, string organizationUrn, CancellationToken cancellationToken)
        => JsonResource(
            $"linkedin://organizations/{connectionId}/{organizationUrn}",
            await connectionService.GetOrganizationAsync(Guid.Parse(connectionId), organizationUrn, cancellationToken));

    private static TextResourceContents JsonResource(string uri, object payload)
        => new()
        {
            Uri = uri,
            MimeType = "application/json",
            Text = JsonSerializer.Serialize(payload, new JsonSerializerOptions(JsonSerializerDefaults.Web)
            {
                WriteIndented = true
            })
        };
}
