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
- `linkedin_refresh_connection`
- `linkedin_delete_stored_data`

Current MCP resources:
- `linkedin://connections/{connectionId}/capabilities`
- `linkedin://connections/{connectionId}/profile`
- `linkedin://organizations/{connectionId}/{organizationUrn}`

Supporting HTTP endpoints:
- `GET /` for a simple service manifest
- `GET /auth/linkedin/callback` for OAuth completion
- `GET /webhooks/linkedin` for LinkedIn webhook challenge validation
- `POST /webhooks/linkedin` for signed webhook delivery intake
- `/health` and `/alive` for health probes

## Current scope and limitations

The repository already exposes the MCP contract for member posting, organization posting, and post analytics, but those tool handlers are intentionally stubbed with structured `not_implemented` results. The live OAuth, capability, profile, organization cache, token refresh, deletion, and webhook flows are implemented; the full posting and reporting payload contracts still need to be wired to approved LinkedIn APIs.

## Roadmap

The current repository is the v1 foundation: it already covers the MCP transport, OAuth start/completion, token lifecycle basics, capability evaluation, profile reads, webhook validation/intake, deletion workflows, and background job processing.

V2 and V3 close the remaining gap to the original `design.md` in stages. V2 focuses on making the server genuinely useful for LinkedIn community-management workflows, while V3 expands into marketing-scale, rate-limit-aware, and approval-gated enterprise capabilities.

### V2

V2 turns the current foundation into a production-ready community-management release.

- Replace the reserved post and analytics handlers with real LinkedIn execution for member posts, organization posts, and post analytics.
- Add live organization sync against LinkedIn organization and role/ACL endpoints so cached organization data is populated and refreshed automatically.
- Introduce a proper LinkedIn connector layer for both `/v2` and `/rest`, including centralized `Linkedin-Version`, `X-Restli-Protocol-Version`, URL/key encoding, and query-tunneling support.
- Add token introspection, stronger token lifecycle handling, and clearer upstream error normalization for expiry, revocation, missing scope, and deprecated API versions.
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

## Repository status

- Build status in this workspace: `dotnet build LinkedInMCP.slnx` passed
- Test status in this workspace: `dotnet test tests/LinkedInMcp.Server.Tests/LinkedInMcp.Server.Tests.csproj --no-build` passed
- No `LICENSE` file or contribution guide is included in the repository yet
