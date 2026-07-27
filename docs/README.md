# Repository Guide

## Project Structure

`src/MX.Caching.sln` contains four packable libraries and the unit test project:

* `MX.Caching.Abstractions` defines public caching contracts.
* `MX.Caching` provides the core composition package.
* `MX.Caching.TableStorage` reserves the Azure Table Storage integration boundary.
* `MX.Caching.Testing` provides consumer-facing testing helpers.
* `MX.Caching.Tests` contains unit tests.

All projects target .NET 9 and .NET 10. Package versions are centrally managed in `Directory.Packages.props`, while `version.json` is the Nerdbank.GitVersioning source for releases.

## Phase 0 Scope

This repository establishes package boundaries, packaging metadata, validation, and release automation. It deliberately does not yet implement caching behavior or Azure Table Storage operations.

## Validation

```pwsh
dotnet build src/MX.Caching.sln
dotnet test src/MX.Caching.sln --filter "FullyQualifiedName!~IntegrationTests"
dotnet format src/MX.Caching.sln --verify-no-changes
```