param(
    [string]$Target,
    [string]$Version,
    [string]$Remote = 'origin'
)

$ErrorActionPreference = 'Stop'

$targets = [ordered]@{
    'dotnet' = 'v'
    'nuxt-oidc' = 'nuxt-v'
    'nuxt-headless' = 'nuxt-headless-v'
    'next-oidc' = 'next-oidc-v'
    'next-headless' = 'next-headless-v'
    'core' = 'core-v'
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

    $tag = "$($targets[$Target])$Version"
    git rev-parse --verify --quiet "refs/tags/$tag" | Out-Null
    if ($LASTEXITCODE -eq 0) {
        throw "Tag '$tag' already exists locally."
    }

    git ls-remote --exit-code --tags $Remote "refs/tags/$tag" | Out-Null
    if ($LASTEXITCODE -eq 0) {
        throw "Tag '$tag' already exists on '$Remote'."
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