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
