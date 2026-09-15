param(
    [string]$Target,
    [string]$Version,
    [string]$Remote = 'origin'
)

$ErrorActionPreference = 'Stop'

$targets = [ordered]@{
    'dotnet' = @{ Prefix = 'v'; Package = $null }
    'nuxt-oidc' = @{ Prefix = 'nuxt-v'; Package = 'src/javascript/nuxt/nuxt-huia-oidc/package.json'; Lock = 'src/javascript/nuxt/nuxt-huia-oidc/package-lock.json' }
    'nuxt-headless' = @{ Prefix = 'nuxt-headless-v'; Package = 'src/javascript/nuxt/nuxt-huia-headless/package.json'; Lock = 'src/javascript/nuxt/nuxt-huia-headless/package-lock.json' }
    'next-oidc' = @{ Prefix = 'next-oidc-v'; Package = 'src/javascript/next/next-huia-oidc/package.json'; Lock = 'src/javascript/next/next-huia-oidc/package-lock.json' }
    'next-headless' = @{ Prefix = 'next-headless-v'; Package = 'src/javascript/next/next-huia-headless/package.json'; Lock = 'src/javascript/next/next-huia-headless/package-lock.json' }
    'core' = @{ Prefix = 'core-v'; Package = 'src/javascript/shared/huia-auth-core/package.json'; Lock = 'src/javascript/shared/huia-auth-core/package-lock.json' }
}

if (-not $Target) {
    Write-Host 'Select the package to publish:'
    $names = @($targets.Keys)
    for ($index = 0; $index -lt $names.Count; $index++) {
        Write-Host "  $($index + 1). $($names[$index])"
    }

    $selection = Read-Host 'Package'
    if ($selection -as [int] -and [int]$selection -ge 1 -and [int]$selection -le $names.Count) {
        $Target = $names[[int]$selection - 1]
    } else {
        $Target = $selection
    }
}

if (-not $targets.Contains($Target)) {
    throw "Unknown target '$Target'. Choose one of: $($targets.Keys -join ', ')."
}

if (-not $Version) {
    $Version = Read-Host 'Version (for example 1.2.3 or 1.2.3-alpha.1)'
}

if ($Version -notmatch '^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(-[0-9A-Za-z-]+(\.[0-9A-Za-z-]+)*)?(\+[0-9A-Za-z-]+(\.[0-9A-Za-z-]+)*)?$') {
    throw "Invalid version '$Version'. Use semantic versioning such as 1.2.3 or 1.2.3-alpha.1."
}

$repoRoot = Split-Path -Parent $PSScriptRoot
Push-Location $repoRoot
try {
    git rev-parse --show-toplevel | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw 'This script must run inside the Huia Git repository.'
    }

    git ls-remote --exit-code $Remote | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "Git remote '$Remote' was not found or is not reachable."
    }

    $targetInfo = $targets[$Target]
    $tag = "$($targetInfo.Prefix)$Version"
    git rev-parse --verify --quiet "refs/tags/$tag" | Out-Null
    if ($LASTEXITCODE -eq 0) {
        throw "Tag '$tag' already exists locally."
    }

    git ls-remote --exit-code --tags $Remote "refs/tags/$tag" | Out-Null
    if ($LASTEXITCODE -eq 0) {
        throw "Tag '$tag' already exists on '$Remote'."
    }

    if ($targetInfo.Package) {
        $packagePath = Join-Path $repoRoot $targetInfo.Package
        $packageJson = Get-Content $packagePath -Raw | ConvertFrom-Json
        $lockPath = Join-Path $repoRoot $targetInfo.Lock
        $lockJson = Get-Content $lockPath -Raw | ConvertFrom-Json -AsHashtable

        $packageJson.version = $Version
        $lockJson['version'] = $Version
        $lockJson['packages']['']['version'] = $Version

        $packageJson | ConvertTo-Json -Depth 100 | Set-Content $packagePath
        $lockJson | ConvertTo-Json -Depth 100 | Set-Content $lockPath

        git add $targetInfo.Package $targetInfo.Lock
        $stagedFiles = @(git diff --cached --name-only)
        if ($stagedFiles | Where-Object { $_ -notin @($targetInfo.Package, $targetInfo.Lock) }) {
            git reset $targetInfo.Package $targetInfo.Lock | Out-Null
            throw 'Unrelated files are already staged; refusing to create a mixed release commit.'
        }
        git diff --cached --quiet
        if ($LASTEXITCODE -eq 0) {
            throw "Package version is already '$Version' in '$($targetInfo.Package)'."
        }

        git commit -m "chore($Target): bump version to $Version"
        git push $Remote HEAD
        if ($LASTEXITCODE -ne 0) {
            throw "Failed to push the version bump to '$Remote'; the tag was not created."
        }
    }

    git tag -a $tag -m "Release $tag"
    git push $Remote $tag
    if ($LASTEXITCODE -ne 0) {
        git tag -d $tag | Out-Null
        throw "Failed to push '$tag'; the local tag was removed."
    }

    Write-Host "Published tag '$tag'. GitHub Actions will now handle the release."
} finally {
    Pop-Location
}