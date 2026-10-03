# Contributing to Huia

Thank you for your interest in contributing to Huia! This document provides guidelines for contributing code, tests, documentation, and reporting issues.

## Code of Conduct

Please treat everyone with respect, kindness, and empathy. Be constructive in feedback and discussions.

## Getting Started

Huia is a monorepo consisting of:
- **.NET 10 Solution**: `src/dotnet/Huia.slnx`
- **TypeScript Packages**: `src/javascript/` (`huia-auth-core`, `next-huia-*`, `nuxt-huia-*`)
- **Samples & E2E**: `samples/`, `tests/Huia.E2ETests`
- **Documentation**: `docs/` (VitePress)

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download) (pinned in `global.json`)
- [Node.js 22+](https://nodejs.org/) and `npm`
- Git

### Building and Testing

#### .NET

From repository root or `src/dotnet`:

```bash
cd src/dotnet
dotnet build Huia.slnx -c Release
dotnet test  Huia.slnx -c Release --filter "Category!=Container&Category!=E2E&Category!=PenTest"
```

To run pen-tests:
```bash
dotnet test tests/Huia.Tests.PenTest/Huia.Tests.PenTest.csproj
```

#### JavaScript & TypeScript

```bash
# Core package (must be built first)
cd src/javascript/shared/huia-auth-core
npm install
npm test
npm run build

# Next.js packages
cd ../../next/next-huia-oidc
npm install && npm test && npm run build

cd ../next-huia-headless
npm install && npm test && npm run build

# Nuxt modules
cd ../../nuxt/nuxt-huia-oidc
npm install && npm test && npm run build

cd ../nuxt-huia-headless
npm install && npm test && npm run build
```

## Pull Request Guidelines

1. **Create a topic branch**: Branch off `main` (e.g., `git checkout -b feature/my-feature` or `fix/issue-123`).
2. **Keep changes focused**: One feature or bugfix per PR.
3. **Write tests**:
   - .NET changes require unit or integration tests in `src/dotnet/tests/`.
   - TypeScript changes require tests in the corresponding `test/` directory.
   - Any security-sensitive changes should include dedicated pen-tests or integration tests.
4. **Ensure CI passes**: Run tests and linting locally before opening a pull request.
5. **Follow code style**:
   - C#: Formatted according to `.editorconfig` rules.
   - TypeScript: Follow existing module style conventions (`vitest`, standard ESLint).

## Releasing and Versioning

Huia uses a **unified versioning model**. All packages across the monorepo (.NET NuGet and NPM) share the same version number and are published in lockstep:
- **.NET NuGet packages**: `Huia`, `Huia.OpenId`, `Huia.OpenId.EntityFrameworkCore`, `Huia.Headless`, `Huia.Headless.EntityFrameworkCore`
- **NPM packages**: `huia-auth-core`, `next-huia-oidc`, `next-huia-headless`, `nuxt-huia-oidc`, `nuxt-huia-headless`

### Creating a Release

To publish a release across all packages:

1. Run the publish script with the target version (e.g. `1.0.0-alpha.16` or `1.0.0`):
   - **Bash**: `./scripts/publish.sh 1.0.0-alpha.16`
   - **PowerShell**: `./scripts/publish.ps1 -Version 1.0.0-alpha.16`
2. The script updates all `package.json` and `package-lock.json` files, commits `chore(release): bump version to <version>`, and creates git tag `v<version>`.
3. Pushing the tag `v<version>` triggers the GitHub Actions `.github/workflows/release.yml` workflow, which:
   - Builds, tests, and publishes all NuGet packages to NuGet.org.
   - Builds, tests, and publishes all NPM packages to npm registry.
   - Creates a consolidated GitHub Release with generated release notes and attached `.nupkg` artifacts.

## Reporting Bugs and Feature Requests

- Check existing issues and PRs before creating a new issue to avoid duplicates.
- Provide minimal reproduction steps and environment details (OS, .NET version, browser/Node version).
- For security vulnerabilities, follow the guidelines in [SECURITY.md](SECURITY.md).

