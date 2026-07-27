# AGENTS.md - dotnet-caching

Multi-target .NET NuGet library repository for shared MX caching abstractions, integrations, and testing helpers.

## Required reading (read these first)

1. `.github/copilot-instructions.md` - repository orientation
2. `.github-copilot/.github/instructions/personal.working-preferences.instructions.md` - Fraser's always-on rules
3. `.github-copilot/.github/copilot-instructions.md` - organization context catalog
4. Stack-specific instruction files listed below

## Org conventions via MCP (when available)

If a `frasermolyneux-copilot` MCP server is configured in your client (`~/.copilot/mcp-config.json`, VS Code user `mcp.json`, or an equivalent stdio MCP wire-up), **prefer its catalog tools** over your own assumptions when answering questions about org standards, branching, workflows, Terraform, .NET projects, Azure patterns, or shared library / platform consumption contracts. The catalog source-of-truth lives in `frasermolyneux/.github-copilot` - see `mcp-server/README.md` there for the tool contract.

This is **complementary** to the file-load model: if `./.github-copilot/` is checked out in the runner (per `copilot-setup-steps.yml`), continue to read those files directly. If both are available, prefer MCP for freshness. If no MCP server is configured in your client, treat this section as a no-op and fall back to the file paths above.

## Stack guardrails

- `standards.dotnet-project`, `standards.editorconfig`
- `dotnet-nuget-library`, `patterns.nbgv-versioning`
- `workflows.dotnet`, `workflows.release-version-and-tag`, `workflows.release-publish-nuget`

## Build, test, and format

```pwsh
dotnet build src/MX.Caching.sln
dotnet test src/MX.Caching.sln --filter "FullyQualifiedName!~IntegrationTests"
dotnet test src/MX.Caching.sln --filter "FullyQualifiedName~MyTestClass.MyTestMethod"
dotnet format src/MX.Caching.sln --verify-no-changes
```

## Do NOT

- Do not `git commit`, `git push`, force-push, rebase, reset --hard, or create/delete branches.
- Do not introduce secrets, tokens, connection strings, or hard-coded credentials.
- Do not bypass build, test, or format checks.
- Do not add caching behavior without an explicit work package.
- Do not put package versions in project files; use `Directory.Packages.props`.

## Pre-PR checks

- [ ] Build succeeds.
- [ ] Tests pass, excluding integration tests.
- [ ] Format check passes.
- [ ] Package output includes all four package projects.
- [ ] `code-review` sub-agent has no unresolved High or Medium findings.

## Escalation

Stop and ask for direction if the work requires a published contract change, a new NuGet package dependency, implementation outside the agreed work package, or an unresolved High review finding.
