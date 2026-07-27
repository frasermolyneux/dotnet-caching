# MX Caching

[![Build and Test](https://github.com/frasermolyneux/dotnet-caching/actions/workflows/build-and-test.yml/badge.svg)](https://github.com/frasermolyneux/dotnet-caching/actions/workflows/build-and-test.yml)
[![Code Quality](https://github.com/frasermolyneux/dotnet-caching/actions/workflows/codequality.yml/badge.svg)](https://github.com/frasermolyneux/dotnet-caching/actions/workflows/codequality.yml)
[![Copilot Setup Steps](https://github.com/frasermolyneux/dotnet-caching/actions/workflows/copilot-setup-steps.yml/badge.svg)](https://github.com/frasermolyneux/dotnet-caching/actions/workflows/copilot-setup-steps.yml)
[![Dependabot Auto-Merge](https://github.com/frasermolyneux/dotnet-caching/actions/workflows/dependabot-automerge.yml/badge.svg)](https://github.com/frasermolyneux/dotnet-caching/actions/workflows/dependabot-automerge.yml)
[![PR Verify](https://github.com/frasermolyneux/dotnet-caching/actions/workflows/pr-verify.yml/badge.svg)](https://github.com/frasermolyneux/dotnet-caching/actions/workflows/pr-verify.yml)
[![Release - Publish NuGet](https://github.com/frasermolyneux/dotnet-caching/actions/workflows/release-publish-nuget.yml/badge.svg)](https://github.com/frasermolyneux/dotnet-caching/actions/workflows/release-publish-nuget.yml)
[![Release - Version and Tag](https://github.com/frasermolyneux/dotnet-caching/actions/workflows/release-version-and-tag.yml/badge.svg)](https://github.com/frasermolyneux/dotnet-caching/actions/workflows/release-version-and-tag.yml)

## Documentation

* [Repository Guide](docs/README.md) - Project structure, package boundaries, and validation commands

## Overview

MX Caching is the shared .NET package foundation for caching in the MX ecosystem. The solution targets .NET 9 and .NET 10, uses Central Package Management, and is versioned with Nerdbank.GitVersioning. It contains package boundaries for abstractions, core composition, Azure Table Storage integration, and consumer-facing testing helpers. This Phase 0 repository is intentionally a scaffold; caching behavior is introduced in later work packages.

## NuGet Packages

| Package                                                                             | Latest                                                                                                                          | Description                                         |
| ----------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------- | --------------------------------------------------- |
| [`MX.Caching.Abstractions`](https://www.nuget.org/packages/MX.Caching.Abstractions) | [![NuGet](https://img.shields.io/nuget/v/MX.Caching.Abstractions.svg)](https://www.nuget.org/packages/MX.Caching.Abstractions/) | Shared contracts for caching integrations.          |
| [`MX.Caching`](https://www.nuget.org/packages/MX.Caching)                           | [![NuGet](https://img.shields.io/nuget/v/MX.Caching.svg)](https://www.nuget.org/packages/MX.Caching/)                           | Core caching composition package.                   |
| [`MX.Caching.TableStorage`](https://www.nuget.org/packages/MX.Caching.TableStorage) | [![NuGet](https://img.shields.io/nuget/v/MX.Caching.TableStorage.svg)](https://www.nuget.org/packages/MX.Caching.TableStorage/) | Azure Table Storage caching integration package.    |
| [`MX.Caching.Testing`](https://www.nuget.org/packages/MX.Caching.Testing)           | [![NuGet](https://img.shields.io/nuget/v/MX.Caching.Testing.svg)](https://www.nuget.org/packages/MX.Caching.Testing/)           | Test helpers for consumers of the caching packages. |

## Contributing

Please read the [contributing](CONTRIBUTING.md) guidance; this is a learning and development project.

## Security

Please read the [security](SECURITY.md) guidance; I am always open to security feedback through email or opening an issue.
