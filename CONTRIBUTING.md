# Contributing

Thanks for your interest in contributing to LinkedInMCP.

This repository is open source, but contributions are intentionally gated to keep the codebase legally clean, reviewable, and aligned with the project roadmap. Not every contribution will be accepted.

## Before you contribute

- For non-trivial work, open an issue or discussion first so the change can be scoped before you spend time implementing it.
- Keep pull requests focused. Small, reviewable changes are preferred over broad refactors.
- If your change affects behavior, include tests or explain why tests are not practical.
- If your change affects user-facing behavior or project policy, update the README or related docs in the same pull request.

## Legal requirements

This project is licensed under [Apache-2.0](./LICENSE).

By contributing, you agree that your contribution is submitted under the same license terms unless explicitly agreed otherwise with the maintainers.

This repository uses the **Developer Certificate of Origin (DCO)** for contribution sign-off.

Every commit in a pull request must include a `Signed-off-by` line. The easiest way to do that is:

```bash
git commit -s -m "Your commit message"
```

That certifies that you have the right to submit the work under the project license. See <https://developercertificate.org/>.

Do not contribute:
- code, content, or assets you do not have the right to license
- copied material from third-party projects without preserving required notices and license compatibility
- secrets, credentials, access tokens, or private customer/member data

## Development workflow

### Prerequisites

- .NET SDK `10.0.104` or later in the stable `10.0.x` line
- Docker Desktop or another local container runtime for Aspire-managed local services

### Build

```bash
dotnet build LinkedInMCP.slnx
```

### Test

```bash
dotnet test tests/LinkedInMcp.Server.Tests/LinkedInMcp.Server.Tests.csproj --no-build
```

### Run locally

```bash
dotnet run --project src/LinkedInMcp.AppHost
```

## Contribution guidelines

- Match the existing architecture: Aspire AppHost, ASP.NET Core MCP server, shared core services, and worker-based background processing.
- Use the official C# MCP SDK patterns already present in the repo instead of introducing custom protocol layers.
- Prefer additive, capability-driven changes over broad speculative feature work.
- Keep approval-gated LinkedIn APIs clearly separated from generally available flows.
- Document any new environment variables, external dependencies, or operational assumptions.

## Pull request expectations

Pull requests are more likely to be accepted when they:
- are tied to an agreed issue or roadmap item
- include tests for new logic where feasible
- keep legal and compliance risk low
- do not expand scope into restricted LinkedIn APIs without clear approval handling

Maintainers may decline contributions that are out of scope, too broad to review safely, legally unclear, or misaligned with the project direction.

## Community standards

By participating in this project, you agree to follow the [Code of Conduct](./CODE_OF_CONDUCT.md).

For security issues, do **not** open a public issue. Follow [SECURITY.md](./SECURITY.md).
