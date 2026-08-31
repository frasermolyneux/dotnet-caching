# dotnet-caching

Multi-target .NET library repository for shared caching abstractions, composition, Azure Table Storage integration, and consumer testing helpers.

## Locations

- Solution: `src/MX.Caching.sln`
- Packages: `src/MX.Caching.Abstractions`, `src/MX.Caching`, `src/MX.Caching.TableStorage`, `src/MX.Caching.Testing`
- Unit tests: `src/MX.Caching.Tests`
- Azurite integration tests: `src/MX.Caching.IntegrationTests`
- Repository guide: `docs/README.md`

## Commands

```pwsh
dotnet build src/MX.Caching.sln
dotnet test src/MX.Caching.sln --filter "FullyQualifiedName!~IntegrationTests"
dotnet test src/MX.Caching.sln --filter "FullyQualifiedName~MyTestClass.MyTestMethod"
dotnet format src/MX.Caching.sln --verify-no-changes
```

## Constraints

- Preserve public caching contracts and the boundaries between abstractions, composition, storage integration, and testing helpers.
- `MX.Caching.Testing` is a published consumer surface, not an internal test-only project.
- Keep package versions centralized in `Directory.Packages.props`; project `PackageReference` entries remain versionless.
- Keep package identities, target frameworks, package READMEs, and `version.json` behavior unchanged unless explicitly requested.
- Build generates packages; do not publish them during validation.
- Run Azurite-backed integration tests only when Table Storage behavior requires them.

## Documentation

- [Repository guide](docs/README.md)
- [Package overview](README.md)
