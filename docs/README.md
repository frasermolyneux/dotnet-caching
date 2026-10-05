# Repository Guide

## Project Structure

`src/MX.Caching.slnx` contains four packable libraries, unit tests, and integration tests:

* `MX.Caching.Abstractions` defines public caching contracts.
* `MX.Caching` provides the core composition package.
* `MX.Caching.TableStorage` reserves the Azure Table Storage integration boundary.
* `MX.Caching.Testing` provides consumer-facing testing helpers.
* `MX.Caching.Tests` contains unit tests.
* `MX.Caching.IntegrationTests` contains Azure Table Storage integration tests.

All projects target .NET 9 and .NET 10. Package versions are centrally managed in `Directory.Packages.props`, while `version.json` is the Nerdbank.GitVersioning source for releases.

## Source-bound Sonar analysis

`codequality.yml` uses the immutable `repository-analysis-sonar/v1.0.1` workflow
from `frasermolyneux/actions`, with profile, recipe and build inputs projected by
the `platform-workloads` catalog. It retains the protected
`quality / Code Quality` check, main/ready-PR/weekly triggers, SDKs, source directory
and format policy. Analysis does not publish packages or request cloud credentials.
Current PR heads can supersede older PR runs; default-branch analyses are not cancelled,
matching the catalog cadence policy.

The pinned native collector runs the existing non-integration unit selection and
binds Cobertura to the actual checkout and successful executed tests. The scanner
requires a completed same-source Sonar task. Default-branch coverage import is
independently checked; PR coverage remains collected until PR-specific server
import verification is accepted. Collection is not claimed as provider import.
The source-bound proof artifact expires after 14 days.

This is the Sonar component of the estate alignment, not completion of the full
analysis profile. Existing secure scanning, dependency review, separate build/test
and Azurite integration behavior remain unchanged. Full-profile daily freshness,
weekly reuse, standard dispatch and other scanner migration remain workstream items.
Do not change the Terraform-owned profile variables or repository protections here.

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
