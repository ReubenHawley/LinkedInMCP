# Reflections
This file contains structured STARR reflections written after tasks to capture learnings, decisions, and reproducible state.

## Bootstrap LinkedIn MCP server - 2026-03-15T09:28:29.6734517+01:00
### Situation
The repository only contained `design.md`, and the user wanted the design turned into a real C# MCP server on the official SDK with .NET Aspire in the inner loop. The design document described a custom HTTP API shape, so I needed to translate that into MCP tools/resources without hand-rolling transport behavior.

### Task
I needed to scaffold and implement the first working version of the LinkedIn MCP server, including the solution layout, Aspire AppHost, ASP.NET Core MCP host, shared core services, worker process, OAuth/webhook plumbing, and a basic test layer. I also needed to keep the implementation on stable .NET 10 and verify it with build and tests.

### Action
I read the local design and relevant skills first, then verified the installed .NET SDKs and checked the official C# MCP and Aspire docs to confirm package choices and hosting patterns. I scaffolded the solution and projects, added the official `ModelContextProtocol.AspNetCore` package, added Aspire AppHost packages, created a shared core library for EF models and LinkedIn services, implemented OAuth URL generation and callback exchange, implemented webhook challenge/signature handling plus durable background jobs, exposed the MCP tool/resource surface in the server, wired the worker to process queued jobs, pinned the repo to stable `10.0.104`, and then ran `dotnet build` and `dotnet test`.

### Result
The repository now contains a working .NET 10 solution with:
- Aspire AppHost orchestrating the MCP server, worker, PostgreSQL, and Redis
- An ASP.NET Core MCP server using the official C# SDK at `/mcp`
- Shared core services for options, persistence, capability mapping, OAuth handling, webhook handling, and background jobs
- A worker loop that leases and processes durable jobs
- Initial tests covering capability mapping and webhook signature validation

The final verification passed with `dotnet build LinkedInMCP.slnx` and `dotnet test tests/LinkedInMcp.Server.Tests/LinkedInMcp.Server.Tests.csproj --no-build`.

### Reflection
The main friction points were package/bootstrap realism and the size of the initial patch set. The large monolithic patch was brittle, so switching to smaller patches was the right recovery. Another important catch was that the build initially drifted onto the preview SDK even though a stable SDK was installed; tightening `global.json` fixed that and preserved the user's “latest stable” requirement.

### State & Artifacts
- Solution: `LinkedInMCP.slnx`
- Stable SDK pin: `global.json`
- Aspire host: `src/LinkedInMcp.AppHost`
- MCP server: `src/LinkedInMcp.Server`
- Shared core: `src/LinkedInMcp.Core`
- Worker: `src/LinkedInMcp.Worker`
- Tests: `tests/LinkedInMcp.Server.Tests`
- Key commands run successfully:
  - `dotnet build LinkedInMCP.slnx`
  - `dotnet test tests/LinkedInMcp.Server.Tests/LinkedInMcp.Server.Tests.csproj --no-build`

### Next-Time Rules
- If a design document describes a custom HTTP facade but the user asked for a real MCP server, then I will translate the behavior into MCP tools/resources early instead of implementing the custom API shape literally.
- If a repo needs “latest stable” .NET and both stable and preview SDKs are installed, then I will pin `global.json` with `allowPrerelease: false` before doing package restore or build validation.
- If a large multi-file patch starts failing on file matching, then I will switch immediately to smaller file-grouped patches instead of retrying the monolith.
- If shared behavior is required by both a web host and a worker, then I will create a shared core project before wiring features so the boundaries stay clean and verification is simpler.

## Add README roadmap - 2026-03-15T09:47:47.0648322+01:00
### Situation
The repository already had a new README and a first working v1 implementation, but it did not explain how the current state maps to the larger `design.md` vision. The user wanted that gap turned into a v2/v3 roadmap and surfaced directly in the README.

### Task
I needed to read the current repo and the design document, derive a roadmap that is faithful to both, and add it to the README without overstating what the code already does.

### Action
I inspected the current README, MCP tool surface, connection service, and `design.md` to separate implemented v1 behavior from planned work. I then inserted a new `## Roadmap` section after the current limitations section, with a short summary plus explicit `### V2` and `### V3` subsections. I kept V2 focused on real community-management execution and V3 focused on marketing-scale, rate-limited, approval-gated expansion.

### Result
The README now gives readers a clear progression:
- v1 = current foundation
- v2 = production-ready posting, org sync, analytics, connector correctness, and stronger verification
- v3 = marketing APIs, rate-limit/caching hardening, tenant-aware policy, and approval-dependent partner surfaces

### Reflection
This was a documentation-only change, but the main risk was accuracy drift. The roadmap needed to be specific enough to be useful while staying honest about what is live today. Anchoring the version split to current code plus `design.md` kept the roadmap concrete instead of aspirational fluff.

### State & Artifacts
- Updated file: `README.md`
- Reference inputs:
  - `design.md`
  - `src/LinkedInMcp.Server/Mcp/LinkedInTools.cs`
  - `src/LinkedInMcp.Core/Services/LinkedInConnectionService.cs`
- No build or test rerun was necessary because this change only touched documentation.

### Next-Time Rules
- If a user asks for a roadmap in a README, then I will derive version boundaries from the current implementation gaps instead of writing generic future-feature bullets.
- If a roadmap touches gated external APIs, then I will phrase those items as approval-dependent unless the repo already proves that access exists.

## Add open source policy files - 2026-03-15T09:56:34.8984461+01:00
### Situation
The user decided to open the repository to outside contributors but wanted to gate contributions and reduce legal risk. The repo did not yet include a license, contribution rules, conduct standards, or a security disclosure policy.

### Task
I needed to add an Apache 2.0 license and create `CONTRIBUTING.md`, `CODE_OF_CONDUCT.md`, and `SECURITY.md` in a way that supports open-source collaboration without inventing unsupported contacts or overpromising process.

### Action
I checked the repo state, confirmed the files were missing, used the official Apache 2.0 text, and added a contribution guide that requires DCO sign-off and sets expectations for scoped PRs, tests, and legal cleanliness. I added a Contributor Covenant based code of conduct and a conservative security policy that points reporters to private GitHub reporting paths instead of inventing an email address. I also updated the README so it now links to the new policy files and reflects that the repository is licensed.

### Result
The repository now has the baseline policy package expected for an open-source project:
- `LICENSE` with Apache-2.0
- `CONTRIBUTING.md` with DCO and PR requirements
- `CODE_OF_CONDUCT.md`
- `SECURITY.md`
- README links and status updated to match

### Reflection
The main constraint was contact information: a security policy and code of conduct often assume a dedicated email, but the repo did not provide one. The safest move was to use GitHub private channels rather than inventing a reporting address. The DCO requirement also fits the user’s stated goal better than a heavier CLA process for now.

### State & Artifacts
- Added: `LICENSE`
- Added: `CONTRIBUTING.md`
- Added: `CODE_OF_CONDUCT.md`
- Added: `SECURITY.md`
- Updated: `README.md`

### Next-Time Rules
- If a repo needs legal/policy files and no reporting email exists, then I will use private GitHub reporting language instead of fabricating a contact method.
- If a user wants open contribution with lightweight gating, then I will pair Apache-2.0 with a DCO-based `CONTRIBUTING.md` before suggesting a heavier CLA process.

## Implement V2 LinkedIn workflows - 2026-03-15T10:31:00.0000000+01:00
### Situation
The repository already had the v1 MCP surface and Aspire scaffold, but the V2 plan from `design.md` was still mostly unimplemented. Posting and analytics tools returned placeholder results, organization access stayed cache-only, token validation was optimistic, and the README still described V2 behavior as future work.

### Task
I needed to turn the V2 plan into real code: add a proper LinkedIn connector and token lifecycle layer, make member/org posting and post analytics live, add explicit organization sync, persist post receipts and token validation state, update the MCP surface and resources, and extend tests around the new workflows.

### Action
I split the old monolithic connection logic into focused services under `src/LinkedInMcp.Core`: `LinkedInApiClient`, `LinkedInTokenService`, `LinkedInOrganizationSyncService`, `LinkedInPostService`, and `LinkedInAnalyticsService`. I extended the data model with validated scopes, token status, member URNs, organization sync metadata, and persisted `PublishedLinkedInPost` records. I rewrote `LinkedInConnectionService` to orchestrate introspection-backed OAuth completion, refresh-aware authorized calls, organization sync, live publishing, analytics retrieval, and revocation handling. On the server side I exposed the new `linkedin_sync_organizations` tool, converted the three stubbed tools into async live workflows, and added the connection organizations resource. I also updated app settings and README so the documented behavior matches the new V2 implementation. During verification, I found that the tiny `LinkedInMcp.ServiceDefaults` project reference was triggering a .NET SDK workload-resolver failure during project-reference evaluation in this sandbox, so I inlined those extensions into the server and worker to keep the actual LinkedIn projects buildable without changing the Aspire AppHost direction.

### Result
The repository now has a real V2 implementation path:
- OAuth completion persists introspected scope and token state.
- Organization sync populates cached org access from LinkedIn ACLs.
- Member and organization posting write real publish receipts.
- Post analytics merges social-action metrics with organization share statistics when available.
- MCP tools/resources expose the new sync and analytics-capable surface.
- Core, server, and worker projects build successfully in the current environment.

The remaining verification gap is environmental rather than code-level: the xUnit test project and Aspire AppHost still hit the SDK workload-resolver bug in this sandbox, so I could not complete a full `dotnet test` or AppHost build here.

### Reflection
The biggest implementation risk was overbuilding against LinkedIn without live credentials. The practical answer was to keep the connector strict on headers, versioning, tunneling, and error normalization, but design it around fakeable HTTP so the behavior can still be validated locally. The second lesson is that tiny helper projects can become build liabilities in constrained environments; inlining the service-default extensions was a better tradeoff than burning more time on the SDK bug.

### State & Artifacts
- Updated: `src/LinkedInMcp.Core/Configuration/LinkedInOptions.cs`
- Updated: `src/LinkedInMcp.Core/Data/Entities.cs`
- Updated: `src/LinkedInMcp.Core/Data/LinkedInMcpDbContext.cs`
- Updated: `src/LinkedInMcp.Core/Models/Contracts.cs`
- Updated: `src/LinkedInMcp.Core/Services/DependencyInjection.cs`
- Updated: `src/LinkedInMcp.Core/Services/LinkedInCapabilityService.cs`
- Replaced: `src/LinkedInMcp.Core/Services/LinkedInConnectionService.cs`
- Added: `src/LinkedInMcp.Core/Services/LinkedInApiClient.cs`
- Added: `src/LinkedInMcp.Core/Services/LinkedInApiException.cs`
- Added: `src/LinkedInMcp.Core/Services/LinkedInTokenService.cs`
- Added: `src/LinkedInMcp.Core/Services/LinkedInOrganizationSyncService.cs`
- Added: `src/LinkedInMcp.Core/Services/LinkedInPostService.cs`
- Added: `src/LinkedInMcp.Core/Services/LinkedInAnalyticsService.cs`
- Added: `src/LinkedInMcp.Core/Services/LinkedInConnectionExtensions.cs`
- Updated: `src/LinkedInMcp.Server/Mcp/LinkedInTools.cs`
- Updated: `src/LinkedInMcp.Server/Mcp/LinkedInResources.cs`
- Updated: `src/LinkedInMcp.Server/Program.cs`
- Updated: `src/LinkedInMcp.Server/LinkedInMcp.Server.csproj`
- Added: `src/LinkedInMcp.Server/ServiceDefaultsExtensions.cs`
- Updated: `src/LinkedInMcp.Server/appsettings.json`
- Updated: `src/LinkedInMcp.Worker/Program.cs`
- Updated: `src/LinkedInMcp.Worker/LinkedInMcp.Worker.csproj`
- Added: `src/LinkedInMcp.Worker/ServiceDefaultsExtensions.cs`
- Updated: `tests/LinkedInMcp.Server.Tests/CapabilityServiceTests.cs`
- Added: `tests/LinkedInMcp.Server.Tests/LinkedInWorkflowTests.cs`
- Updated: `README.md`

### Next-Time Rules
- If a V2 plan depends on a third-party API that cannot be exercised live, then I will implement a fakeable HTTP client layer first so the workflow logic stays testable.
- If project-reference evaluation fails because of SDK workload resolution in a constrained environment, then I will look for the smallest dependency edge to remove before changing larger architecture decisions.

## Fix Aspire dashboard bootstrap defaults - 2026-03-15T10:45:00.0000000+01:00
### Situation
Running the AppHost failed before any project resources started. Aspire threw an `OptionsValidationException` because the dashboard bootstrap expected `ASPNETCORE_URLS` plus at least one OTLP endpoint environment variable, but none were present in the launch path being used.

### Task
I needed to make the AppHost resilient when launched outside an IDE-generated Aspire profile while preserving the intended local inner development loop with the dashboard enabled.

### Action
I checked the AppHost code and package documentation, confirmed the installed Aspire version expects `ASPIRE_DASHBOARD_OTLP_ENDPOINT_URL` / `ASPIRE_DASHBOARD_OTLP_HTTP_ENDPOINT_URL`, and added a bootstrap helper in `src/LinkedInMcp.AppHost/Program.cs` that sets default local dashboard URLs when those variables are absent. I also switched the builder creation to `DistributedApplication.CreateBuilder(new DistributedApplicationOptions { Args = args, AllowUnsecuredTransport = true })` so the default HTTP endpoints are accepted for local development. To make IDE and `dotnet run` launches consistent, I added `src/LinkedInMcp.AppHost/Properties/launchSettings.json` with the same dashboard endpoint values.

### Result
The source now has explicit, stable Aspire dashboard defaults instead of depending on external launch machinery. The AppHost should start with:
- dashboard frontend: `http://127.0.0.1:18888`
- OTLP gRPC: `http://127.0.0.1:18889`
- OTLP HTTP: `http://127.0.0.1:18890`

I could not rebuild the AppHost binary inside this sandbox because the same SDK workload-resolver issue that affected AppHost verification earlier is still blocking direct AppHost builds here, so runtime verification is limited to the source fix.

### Reflection
This failure mode is a good example of why local orchestration projects need explicit defaults even when templates often rely on launch-profile generation. The safest fix was to make the AppHost self-sufficient first and keep the launch profile as reinforcement rather than the only source of truth.

### State & Artifacts
- Updated: `src/LinkedInMcp.AppHost/Program.cs`
- Added: `src/LinkedInMcp.AppHost/Properties/launchSettings.json`

### Next-Time Rules
- If Aspire dashboard startup depends on environment variables that may be absent outside IDE launch profiles, then I will set conservative local defaults in the AppHost source as well as in `launchSettings.json`.
- If an AppHost runtime issue can be isolated to bootstrap configuration, then I will verify against the package docs and patch the launch path before changing service resources or Aspire topology.

## Tighten local LinkedIn auth diagnostics - 2026-03-15T11:20:00.0000000+01:00
### Situation
The local LinkedIn OAuth flow was partly working, but the browser flow was still fragile for manual use. The MCP auth tool output had improved copy guidance, yet a failed LinkedIn browser round-trip could still leave the user with a vague LinkedIn interstitial or an unhelpful callback failure.

### Task
I needed to make the local auth loop easier to debug by surfacing the exact redirect URI in the MCP auth response, returning clearer callback diagnostics when LinkedIn redirects back with OAuth errors, and documenting the product/scope checks that commonly break local development.

### Action
I extended `AuthUrlResponse` with `redirectUri` and `configurationHint`, updated `LinkedInConnectionService.BeginAuthAsync` to populate those fields from the active options, and changed the `/auth/linkedin/callback` endpoint to handle `error` and `error_description` query parameters explicitly with troubleshooting output instead of relying on minimal API parameter binding. I also expanded the README authentication section to call out the exact local callback URL, scope guidance, the Sign In with LinkedIn using OpenID Connect requirement for OIDC scopes, and quick local checks before starting the browser flow. I then rebuilt the core and server projects using a repo-local `DOTNET_CLI_HOME` to avoid the sandbox's first-time-use restriction.

### Result
The MCP auth tool now tells the caller exactly which redirect URI must match the LinkedIn app configuration, and the local callback route returns actionable JSON when LinkedIn redirects back with an OAuth error. The README is also more explicit about scope expectations and local setup checks. Both `LinkedInMcp.Core` and `LinkedInMcp.Server` compile successfully after the changes.

### Reflection
When an OAuth flow spans an MCP client, a browser, a SaaS consent screen, and a local callback endpoint, small mismatches become hard to diagnose quickly. Exposing the active redirect URI and handling callback-side error query parameters directly is a better debugging posture than assuming the user can infer the failure from the provider's interstitial alone.

### State & Artifacts
- Updated: `src/LinkedInMcp.Core/Models/Contracts.cs`
- Updated: `src/LinkedInMcp.Core/Services/LinkedInConnectionService.cs`
- Updated: `src/LinkedInMcp.Server/Program.cs`
- Updated: `README.md`

### Next-Time Rules
- If an OAuth tool returns a browser URL, then I will also return the exact redirect URI and a concise configuration hint so app-side mismatches are easier to spot.
- If a callback endpoint may receive provider-side OAuth errors, then I will parse and surface those query parameters explicitly instead of relying on default parameter binding.
