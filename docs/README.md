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

`codequality.yml` uses the immutable `repository-analysis-sonar/v1.1.2` workflow
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

The producer additionally verifies source/finding facts for the same completed
analysis. Default `proof.facts.scope: branch-source-and-findings` requires positive
maintained-file counts for every selected Sonar capability and binds the latest
exact analysis ID/revision. PR `proof.facts.scope: pull-request-incremental` binds
the actual merge checkout and receipt task before and after reads using Execute
Analysis and Browse only. Its whole-branch `proof.facts.sourceCoverage` is explicitly
null, mirrored at `proof.sourceCoverage`, with status `incremental-pr-only` and a
visible limitation; only actually returned metadata contributes to reported file
counts. An empty incremental PR population is not zero analyzed source or full
scanner completeness. PR facts cannot establish default freshness.

Queued or superseding project analyses, incomplete paging, unowned selected files
and provider errors still fail explicitly rather than borrowing another task's
metadata or default data. Raw findings are scoped to the current PR response or
default backlog, not a new merge gate. Metadata counts are not provider file-byte
attestation or coverage percentages. The outer `proof.scope` remains
`sonar-task-and-selected-coverage-only` with `fullProfileEvidence: false`;
immutable foreign execution and default import remain acceptance gates.

This is the Sonar component of the estate alignment, not completion of the full
profile. Public native analysis uses the independently accepted released component,
with no Sonar token or duplicate Sonar task.

The separate `native-codeql` job now invokes the released
`repository-analysis-codeql/v1.0.0` component with the same catalog profile,
build recipe and source directory. It analyzes the declared Actions and C#
capabilities and requires byte-bound source extraction plus completed current-source
GitHub processing. It receives no Sonar credential. The original CodeQL-only call
is retired after actual foreign PR run `37546285453` and merged-default run
`37547225641` verified source/definition identities, raw artifact receipts and
completed native processing. At accepted default source
`7f938a740a7cf12cec47c6280f176099a4741808`, both capabilities had zero findings:
7 workflow files/17 rules and 33 C# files/52 rules. This evidence is not inferred
from a release tag or a passing fixture. Retirement passed fresh protected checks
and review through `frasermolyneux/dotnet-caching#22`; actual post-retirement
default run `37574765205` at `87d94780aa42ecedb9a6cfcc05f7f8bc2e8f53c5`
independently verified raw receipts and completed processing with the same
capability/rule counts and zero findings. No analysis history or protection was
deleted to permit the transition.
Native component acceptance alone is not full-profile freshness evidence.

Existing secure scanning, dependency review, separate build/test
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
