# Repository Guide

## Project Structure

`src/MX.Caching.slnx` contains four packable libraries and the unit test project:

* `MX.Caching.Abstractions` defines public caching contracts.
* `MX.Caching` provides the core composition package.
* `MX.Caching.TableStorage` reserves the Azure Table Storage integration boundary.
* `MX.Caching.Testing` provides consumer-facing testing helpers.
* `MX.Caching.Tests` contains unit tests.

All projects target .NET 9 and .NET 10. Package versions are centrally managed in `Directory.Packages.props`, while `version.json` is the Nerdbank.GitVersioning source for releases.

## Scope

The repository provides cache composition, Azure Table Storage-backed distributed caching, and consumer-facing testing helpers. Azure Table entries persist their effective expiry, optional absolute-expiry cap, and sliding-expiration metadata.

## Validation

```pwsh
dotnet build src/MX.Caching.slnx
dotnet test src/MX.Caching.slnx --filter "FullyQualifiedName!~IntegrationTests"
dotnet format src/MX.Caching.slnx --verify-no-changes
```

## Integration Tests

The Azure Table Storage integration tests use the Azurite development-storage endpoint (`UseDevelopmentStorage=true`). Start Azurite before running them locally:

```pwsh
azurite
dotnet test src/MX.Caching.IntegrationTests/MX.Caching.IntegrationTests.csproj
```
