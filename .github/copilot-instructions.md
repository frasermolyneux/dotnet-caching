# Copilot Instructions

This repository publishes the MX caching package family: public abstractions, core composition, Azure Table Storage integration, and consumer testing helpers.

## Runtime and layout

- SDK: `10.0.301` from `global.json`; projects inherit `net9.0;net10.0` from `Directory.Build.props`.
- Solution: `src/MX.Caching.sln`.
- Packable projects: `MX.Caching.Abstractions`, `MX.Caching`, `MX.Caching.TableStorage`, and `MX.Caching.Testing`.
- Unit tests: `MX.Caching.Tests`; Azurite-backed tests: `MX.Caching.IntegrationTests`.

## Repository rules

- Keep public contracts in `MX.Caching.Abstractions`; keep storage-specific behavior in `MX.Caching.TableStorage`.
- Treat `MX.Caching.Testing` helpers as a published consumer contract.
- Preserve expiry, absolute-expiry cap, and sliding-expiration metadata behavior for Table Storage entries.
- Central package management is enabled in `Directory.Packages.props`; do not add versions to project `PackageReference` entries.
- Package IDs, target frameworks, package READMEs, generated package metadata, and NBGV configuration in `version.json` are release boundaries.
- Never add credentials or publish packages during routine validation.

## Validation

```pwsh
dotnet build src/MX.Caching.sln
dotnet test src/MX.Caching.sln --filter "FullyQualifiedName!~IntegrationTests"
dotnet test src/MX.Caching.sln --filter "FullyQualifiedName~MyTestClass.MyTestMethod"
dotnet format src/MX.Caching.sln --verify-no-changes
```

Run `src/MX.Caching.IntegrationTests` only when the changed behavior requires Azurite. See `docs/README.md` for package boundaries and integration-test setup.
