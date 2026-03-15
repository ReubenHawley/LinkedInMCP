using System.ComponentModel;
using LinkedInMcp.Core.Models;
using LinkedInMcp.Core.Services;
using ModelContextProtocol.Server;

namespace LinkedInMcp.Server.Mcp;

[McpServerToolType]
public sealed class LinkedInTools(LinkedInConnectionService connectionService)
{
    [McpServerTool, Description("Returns the LinkedIn capability snapshot for a stored connection.")]
    public Task<CapabilitySnapshot> linkedin_get_capabilities(
        [Description("Optional LinkedIn connection id. Defaults to the most recently updated connection.")] string? connectionId = null,
        CancellationToken cancellationToken = default)
        => connectionService.GetCapabilitiesAsync(ParseOptionalGuid(connectionId), cancellationToken);

    [McpServerTool, Description("Starts the LinkedIn OAuth flow and returns the authorization URL.")]
    public Task<AuthUrlResponse> linkedin_begin_auth(
        [Description("Optional scopes to request. When omitted, the server uses the configured defaults.")] string[]? requestedScopes = null,
        [Description("Optional return URL for the caller to track after the browser flow completes.")] string? returnUrl = null,
        CancellationToken cancellationToken = default)
        => connectionService.BeginAuthAsync(requestedScopes, returnUrl, cancellationToken);

    [McpServerTool, Description("Returns the current connection status for a LinkedIn account.")]
    public Task<ConnectionStatusResponse> linkedin_complete_connection_status(
        [Description("Optional LinkedIn connection id. Defaults to the most recently updated connection.")] string? connectionId = null,
        CancellationToken cancellationToken = default)
        => connectionService.GetConnectionStatusAsync(ParseOptionalGuid(connectionId), cancellationToken);

    [McpServerTool, Description("Disconnects a LinkedIn account and queues downstream deletion work.")]
    public Task<DeletionResult> linkedin_disconnect_account(
        [Description("The LinkedIn connection id to disconnect.")] string connectionId,
        CancellationToken cancellationToken = default)
        => connectionService.DeleteStoredDataAsync(ParseRequiredGuid(connectionId), cancellationToken);

    [McpServerTool, Description("Returns the cached LinkedIn profile for the current connection.")]
    public Task<LinkedInProfileSnapshot> linkedin_get_me(
        [Description("Optional LinkedIn connection id. Defaults to the most recently updated connection.")] string? connectionId = null,
        [Description("When true, refresh profile data from LinkedIn userinfo before returning it.")] bool refreshFromLinkedIn = false,
        CancellationToken cancellationToken = default)
        => connectionService.GetProfileAsync(ParseOptionalGuid(connectionId), refreshFromLinkedIn, cancellationToken);

    [McpServerTool, Description("Lists cached LinkedIn organizations for a connection.")]
    public Task<IReadOnlyList<OrganizationAccessSnapshot>> linkedin_list_organizations(
        [Description("The LinkedIn connection id.")] string connectionId,
        CancellationToken cancellationToken = default)
        => connectionService.ListOrganizationsAsync(ParseRequiredGuid(connectionId), cancellationToken);

    [McpServerTool, Description("Returns one cached LinkedIn organization record for a connection.")]
    public Task<OrganizationAccessSnapshot> linkedin_get_organization(
        [Description("The LinkedIn connection id.")] string connectionId,
        [Description("The LinkedIn organization URN.")] string organizationUrn,
        CancellationToken cancellationToken = default)
        => connectionService.GetOrganizationAsync(ParseRequiredGuid(connectionId), organizationUrn, cancellationToken);

    [McpServerTool, Description("Lists cached organization roles for a connection and organization.")]
    public async Task<IReadOnlyList<string>> linkedin_list_organization_roles(
        [Description("The LinkedIn connection id.")] string connectionId,
        [Description("The LinkedIn organization URN.")] string organizationUrn,
        CancellationToken cancellationToken = default)
        => (await connectionService.GetOrganizationAsync(ParseRequiredGuid(connectionId), organizationUrn, cancellationToken)).roles;

    [McpServerTool, Description("Reserved entry point for member post publishing once the live LinkedIn payload contract is enabled.")]
    public OperationResult linkedin_create_member_post(
        [Description("The LinkedIn connection id.")] string connectionId,
        [Description("Plain text post body.")] string text)
        => connectionService.CreateUnsupportedResult("Member post publishing");

    [McpServerTool, Description("Reserved entry point for organization post publishing once the live LinkedIn payload contract is enabled.")]
    public OperationResult linkedin_create_organization_post(
        [Description("The LinkedIn connection id.")] string connectionId,
        [Description("The LinkedIn organization URN.")] string organizationUrn,
        [Description("Plain text post body.")] string text)
        => connectionService.CreateUnsupportedResult("Organization post publishing");

    [McpServerTool, Description("Reserved entry point for LinkedIn post analytics once the live reporting contract is enabled.")]
    public OperationResult linkedin_get_post_analytics(
        [Description("A LinkedIn post URN or server-side post identifier.")] string postId)
        => connectionService.CreateUnsupportedResult("Post analytics");

    [McpServerTool, Description("Refreshes the access token for a stored LinkedIn connection.")]
    public Task<ConnectionStatusResponse> linkedin_refresh_connection(
        [Description("The LinkedIn connection id.")] string connectionId,
        CancellationToken cancellationToken = default)
        => connectionService.RefreshAsync(ParseRequiredGuid(connectionId), cancellationToken);

    [McpServerTool, Description("Queues deletion of stored LinkedIn-derived data for a connection.")]
    public Task<DeletionResult> linkedin_delete_stored_data(
        [Description("The LinkedIn connection id.")] string connectionId,
        CancellationToken cancellationToken = default)
        => connectionService.DeleteStoredDataAsync(ParseRequiredGuid(connectionId), cancellationToken);

    private static Guid? ParseOptionalGuid(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : Guid.Parse(value);

    private static Guid ParseRequiredGuid(string value)
        => Guid.Parse(value);
}
