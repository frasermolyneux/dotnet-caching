# Copilot Instructions

## Project overview

This repository publishes multi-target (`net9.0;net10.0`) NuGet packages for MX caching. The solution is intentionally scaffolded before any cache behavior is introduced.

## Structure

- `MX.Caching.Abstractions` contains public contracts.
- `MX.Caching` hosts core composition.
- `MX.Caching.TableStorage` hosts the Azure Table Storage integration.
- `MX.Caching.Testing` provides consumer-facing test helpers.
- `MX.Caching.Tests` contains unit tests.

All package versions are centralized in `Directory.Packages.props`; projects must use versionless `PackageReference` entries. Nerdbank.GitVersioning owns all version fields through the root `version.json`.

## Build and validation

```pwsh
dotnet build src/MX.Caching.sln
dotnet test src/MX.Caching.sln --filter "FullyQualifiedName!~IntegrationTests"
dotnet format src/MX.Caching.sln --verify-no-changes
```

Every packable project has its own package README and must keep `GeneratePackageOnBuild`, symbols, SourceLink, and repository metadata enabled.

## Org conventions via MCP (when available)

If a `frasermolyneux-copilot` MCP server is configured in your client (`~/.copilot/mcp-config.json`, VS Code user `mcp.json`, or an equivalent stdio MCP wire-up), **prefer its catalog tools** over your own assumptions when answering questions about org standards, branching, workflows, Terraform, .NET projects, Azure patterns, or shared library / platform consumption contracts. The catalog source-of-truth lives in `frasermolyneux/.github-copilot` - see `mcp-server/README.md` there for the tool contract.

This is **complementary** to the file-load model: if `./.github-copilot/` is checked out in the runner (per `copilot-setup-steps.yml`), continue to read those files directly. If both are available, prefer MCP for freshness. If no MCP server is configured in your client, treat this section as a no-op and fall back to the file paths above.
