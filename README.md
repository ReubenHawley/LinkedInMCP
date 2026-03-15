# LinkedInMCP

Remote LinkedIn MCP server built on the official C# Model Context Protocol SDK, with a .NET 10 ASP.NET Core host and a .NET Aspire inner development loop.

This repository turns the LinkedIn integration design into a real MCP service instead of a custom protocol implementation. It gives you an Aspire-composed local stack with an MCP server, a background worker, PostgreSQL, and Redis, plus the LinkedIn-specific HTTP endpoints that MCP clients still need around the edges: OAuth callback handling, webhook validation, and webhook delivery intake.

## Quick start

Prerequisites:
- .NET SDK `10.0.104` or later in the `10.0.x` line
- Docker Desktop or another local container runtime for the Aspire-managed PostgreSQL and Redis containers

```bash
dotnet build LinkedInMCP.slnx
dotnet test tests/LinkedInMcp.Server.Tests/LinkedInMcp.Server.Tests.csproj --no-build
dotnet run --project src/LinkedInMcp.AppHost
```

Expected result: the Aspire AppHost starts the `server` and `worker` projects plus local PostgreSQL and Redis. From the Aspire dashboard, open the `server` service and use its `/mcp` endpoint as the MCP base URL.

## Why this project

- Uses the official `ModelContextProtocol.AspNetCore` package for MCP transport and discovery instead of custom JSON-RPC plumbing.
- Keeps the local inner loop simple with Aspire orchestration for the app host, worker, database, cache, and observability.
- Separates the MCP host from background processing so webhook and deletion work can be queued and processed safely.
- Starts from capability-driven LinkedIn integration: current scopes and enabled features determine what the server exposes and executes.

## What is implemented

Current MCP tools:
- `linkedin_get_capabilities`
- `linkedin_begin_auth`
- `linkedin_complete_connection_status`
- `linkedin_disconnect_account`
- `linkedin_get_me`
- `linkedin_list_organizations`
- `linkedin_get_organization`
- `linkedin_list_organization_roles`
- `linkedin_sync_organizations`
- `linkedin_create_member_post`
- `linkedin_create_organization_post`
- `linkedin_get_post_analytics`
- `linkedin_refresh_connection`
- `linkedin_delete_stored_data`

Current MCP resources:
- `linkedin://connections/{connectionId}/capabilities`
- `linkedin://connections/{connectionId}/profile`
- `linkedin://connections/{connectionId}/organizations`
- `linkedin://organizations/{connectionId}/{organizationUrn}`

Supporting HTTP endpoints:
- `GET /` for a simple service manifest
- `GET /auth/linkedin/callback` for OAuth completion
- `GET /webhooks/linkedin` for LinkedIn webhook challenge validation
- `POST /webhooks/linkedin` for signed webhook delivery intake
- `/health` and `/alive` for health probes

## Current scope and limitations

The repository now executes the V2 community-management workflows from `design.md`: OAuth completion with token introspection, cached organization sync through LinkedIn ACLs, member posting, organization posting with cached role validation, normalized post analytics, token refresh, deletion workflows, and webhook intake.

The main remaining limitations are the broader V3 surfaces: ads and marketing APIs, richer media and targeting support, more exhaustive contract and integration coverage, and additional production hardening around quota governance and multi-tenant policy controls.

## Roadmap

The current repository now includes the V2 community-management baseline on top of the original v1 foundation: MCP transport, OAuth start/completion, token introspection, capability evaluation, profile reads, organization sync, posting, post analytics, webhook validation/intake, deletion workflows, and background job processing.

The remaining roadmap is about deepening that implementation rather than inventing a new surface. V3 expands the server into marketing-scale, rate-limit-aware, and approval-gated enterprise capabilities.

### V2

V2 is the current production-oriented community-management release shape for the repository.

- Real LinkedIn execution is in place for member posts, organization posts, and normalized post analytics.
- Organization sync now reads ACLs and organization details from LinkedIn and persists the cache used by the MCP resources and organization-post preflight.
- The connector layer now centralizes `/v2` and `/rest` handling, `Linkedin-Version`, `X-Restli-Protocol-Version`, error normalization, and GET query tunneling.
- Token lifecycle handling now includes introspection-backed scope validation plus refresh and revocation-aware retries.
- Expand verification from today’s unit tests into contract and integration coverage for OAuth callback, webhook validation/signature handling, posting, organization sync, and analytics reads.

### V3

V3 extends the server from a community-management MCP into a policy-driven LinkedIn platform integration for marketing and other approved enterprise workflows.

- Add approved Marketing API surfaces for ads account access, campaign operations, and ads reporting when the required product approvals and scopes are available.
- Implement the design’s rate-limit ledger, caching/TTL strategy, request coalescing, and retry/backoff policies so the server behaves predictably under LinkedIn’s day-based quotas.
- Add broader operational hardening with structured observability, upstream diagnostics capture, monthly version-governance workflows, and production-grade alerting/runbook coverage.
- Expand the policy and capability engine to support tenant-level entitlements, product flags, and safer degradation for restricted LinkedIn APIs.
- Keep messaging, invitations, connections, and other partner-gated surfaces explicitly approval-dependent rather than treating them as guaranteed roadmap items.

## Installation

### Prerequisites

- .NET `10.0.104` or later in the stable `10.0.x` line
- A container runtime for local Aspire resources
- LinkedIn app credentials only if you want to exercise live OAuth or webhook flows

### Restore and build

```bash
dotnet build LinkedInMCP.slnx
```

### Verify

```bash
dotnet test tests/LinkedInMcp.Server.Tests/LinkedInMcp.Server.Tests.csproj --no-build
```

## Usage

### Run the full local stack

```bash
dotnet run --project src/LinkedInMcp.AppHost
```

The AppHost provisions:
- `server`: the ASP.NET Core MCP host
- `worker`: background job processor
- `postgres`: durable persistence for connections, jobs, webhook deliveries, and deletion requests
- `cache`: Redis for the local dev topology

### Check the server endpoint

After the AppHost is running, copy the `server` base URL from the Aspire dashboard and call the root endpoint:

```bash
curl <server-base-url>/
```

Expected response:

```json
{
  "name": "LinkedIn MCP Server",
  "mcp": "/mcp",
  "callback": "/auth/linkedin/callback",
  "webhook": "/webhooks/linkedin"
}
```

### Connect a LinkedIn account

Use an MCP client against the `/mcp` endpoint, then call:
1. `linkedin_begin_auth` to get the LinkedIn authorization URL
2. Complete the browser flow against your configured LinkedIn app
3. Let LinkedIn redirect to `/auth/linkedin/callback`
4. Call `linkedin_complete_connection_status` or `linkedin_get_capabilities`

### Authentication

For local Aspire runs, this repository assumes the LinkedIn OAuth callback is:

```text
https://localhost:7443/auth/linkedin/callback
```

That value must match in both places:
- your LinkedIn developer app Auth settings
- `LinkedIn:RedirectUri` in local configuration

`linkedin_begin_auth` now returns both `authorizationUrl` and `browserReadyAuthorizationUrl`. Open that URL directly in the browser and copy only the URL value, not the surrounding JSON response.

The auth response also includes `redirectUri` and `configurationHint`. Compare that `redirectUri` value directly against the LinkedIn app Auth tab if the browser flow fails.

Scope guidance:

| Scope | Purpose | Notes |
| --- | --- | --- |
| `openid` | Required for basic LinkedIn sign-in | Minimum scope for the OAuth flow in this repo |
| `profile` | OIDC profile claims | Recommended for display name and profile metadata |
| `email` | OIDC email claim | Optional because LinkedIn may omit email in some cases |
| `w_member_social` | Publish member posts | Required for `linkedin_create_member_post` |
| `rw_organization_admin` | Read and sync organization access | Required for organization role/ACL workflows |
| `w_organization_social` | Publish organization posts | Required for `linkedin_create_organization_post` |
| `r_organization_social` | Read organization post analytics | Used for organization analytics enrichment |
| `r_ads`, `rw_ads`, `r_ads_reporting` | Future ads and reporting surfaces | Approval-dependent and not part of the current V2 workflow |

Recommended scope sets:
- Personal sign-in only: `openid`, `profile`, `email`
- Member posting: `openid`, `profile`, `email`, `w_member_social`
- Organization workflows: `openid`, `profile`, `email`, `rw_organization_admin`, `w_organization_social`, `r_organization_social`

Request only scopes your LinkedIn app is approved to use. LinkedIn may reject or silently limit flows for scopes/products that are not enabled on the app.

Common local auth checks:
- In the LinkedIn developer portal `Auth` tab, the authorized redirect URL must be the exact callback URL: `https://localhost:7443/auth/linkedin/callback`
- In the LinkedIn developer portal `Products` tab, `openid`, `profile`, and `email` require the Sign In with LinkedIn using OpenID Connect product
- Before starting the OAuth flow, open `https://localhost:7443/` locally and confirm the server responds
- If LinkedIn redirects back with an OAuth error, open the callback URL locally and check the JSON error payload from `/auth/linkedin/callback`

### Live configuration

The server can start without LinkedIn credentials, but live OAuth and webhook validation require them.

| Setting | Required | Purpose |
| --- | --- | --- |
| `LinkedIn__ClientId` | For live OAuth | LinkedIn application client id |
| `LinkedIn__ClientSecret` | For live OAuth and webhooks | LinkedIn application client secret used for token exchange and HMAC validation |
| `LinkedIn__RedirectUri` | For live OAuth | Redirect URI that points to `/auth/linkedin/callback` |
| `LinkedIn__DefaultApiVersion` | No | Default LinkedIn REST version header, defaults to `202602` |
| `LinkedIn__Features__UserInfo` | No | Enables userinfo capability evaluation |
| `LinkedIn__Features__MemberPosting` | No | Enables member posting capability checks |
| `LinkedIn__Features__OrganizationPosting` | No | Enables organization posting capability checks |
| `LinkedIn__Features__AdsManagement` | No | Enables ads management capability checks |
| `LinkedIn__Features__AdsReporting` | No | Enables ads reporting capability checks |
| `LinkedIn__Features__Webhooks` | No | Enables webhook capability checks |

If you run the server outside the AppHost, you must also supply `ConnectionStrings__linkedinmcp` yourself. The AppHost injects the local database and cache references automatically.

For the Aspire inner loop, the simplest setup is AppHost user secrets:

```powershell
dotnet user-secrets --project src/LinkedInMcp.AppHost set "LinkedIn:ClientId" "<your-client-id>"
dotnet user-secrets --project src/LinkedInMcp.AppHost set "LinkedIn:ClientSecret" "<your-client-secret>"
dotnet user-secrets --project src/LinkedInMcp.AppHost set "LinkedIn:RedirectUri" "https://localhost:7443/auth/linkedin/callback"
```

The AppHost now forwards those values into the `server` and `worker` projects automatically.

## Project layout

```text
src/
  LinkedInMcp.AppHost         Aspire orchestration for the local stack
  LinkedInMcp.Core            Shared LinkedIn services, EF Core models, and options
  LinkedInMcp.Server          ASP.NET Core MCP host and LinkedIn HTTP endpoints
  LinkedInMcp.ServiceDefaults Shared service defaults for health and ProblemDetails
  LinkedInMcp.Worker          Background job processor
tests/
  LinkedInMcp.Server.Tests    Unit tests for capability and webhook behavior
```

## Docs and next steps

- [design.md](./design.md): original functional and technical design
- [src/LinkedInMcp.Server](./src/LinkedInMcp.Server): MCP host entry point and tool/resource surface
- [src/LinkedInMcp.Core](./src/LinkedInMcp.Core): shared OAuth, webhook, persistence, and capability logic
- [tests/LinkedInMcp.Server.Tests](./tests/LinkedInMcp.Server.Tests): executable verification for current behavior
- [CONTRIBUTING.md](./CONTRIBUTING.md): contribution workflow, DCO requirement, and PR expectations
- [CODE_OF_CONDUCT.md](./CODE_OF_CONDUCT.md): community participation standards
- [SECURITY.md](./SECURITY.md): private vulnerability reporting guidance

## License

Licensed under [Apache License 2.0](./LICENSE).

## Repository status

- Build status in this workspace: `dotnet build LinkedInMCP.slnx` passed
- Test status in this workspace: `dotnet test tests/LinkedInMcp.Server.Tests/LinkedInMcp.Server.Tests.csproj --no-build` passed
- Open-source policy files are now present: `LICENSE`, `CONTRIBUTING.md`, `CODE_OF_CONDUCT.md`, and `SECURITY.md`
