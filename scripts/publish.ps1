param(
    [string]$Version,
    [string]$Remote = 'origin',
    [switch]$Force
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false

if (-not $Version) {
    $Version = Read-Host 'Version to release (for example 1.0.0-alpha.16)'
}

$Version = $Version.Trim() -replace '^v', ''

if ($Version -notmatch '^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(-[0-9A-Za-z-]+(\.[0-9A-Za-z-]+)*)?(\+[0-9A-Za-z-]+(\.[0-9A-Za-z-]+)*)?$') {
    throw "Invalid version '$Version'. Use semantic versioning such as 1.0.0-alpha.16 or 1.0.0."
}

$utf8NoBom = [System.Text.UTF8Encoding]::new($false)

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

    $tag = "v$Version"
    if ($Force) {
        git rev-parse --verify --quiet "refs/tags/$tag" | Out-Null
        if ($LASTEXITCODE -eq 0) {
            git tag -d $tag | Out-Null
            if ($LASTEXITCODE -ne 0) {
                throw "Failed to remove local tag '$tag'."
            }
        }

        git ls-remote --exit-code --tags $Remote "refs/tags/$tag" | Out-Null
        if ($LASTEXITCODE -eq 0) {
            git push $Remote ":refs/tags/$tag"
            if ($LASTEXITCODE -ne 0) {
                throw "Failed to remove remote tag '$tag' from '$Remote'."
            }
        }
    } else {
        git rev-parse --verify --quiet "refs/tags/$tag" | Out-Null
        if ($LASTEXITCODE -eq 0) {
            throw "Tag '$tag' already exists locally."
        }

        git ls-remote --exit-code --tags $Remote "refs/tags/$tag" | Out-Null
        if ($LASTEXITCODE -eq 0) {
            throw "Tag '$tag' already exists on '$Remote'."
        }
    }

    $packages = @(
        'src/javascript/shared/huia-auth-core',
        'src/javascript/next/next-huia-oidc',
        'src/javascript/next/next-huia-headless',
        'src/javascript/nuxt/nuxt-huia-oidc',
        'src/javascript/nuxt/nuxt-huia-headless',
        'src/javascript/tanstack-start/tanstack-start-huia-oidc',
        'src/javascript/tanstack-start/tanstack-start-huia-headless'
    )

    $stagedFiles = @()

    foreach ($pkg in $packages) {
        $packagePath = Join-Path $repoRoot "$pkg/package.json"
        $lockPath = Join-Path $repoRoot "$pkg/package-lock.json"

        $packageJson = Get-Content $packagePath -Raw | ConvertFrom-Json
        $packageJson.version = $Version
        $pkgContent = (($packageJson | ConvertTo-Json -Depth 100) -replace "`r`n", "`n") + "`n"
        [System.IO.File]::WriteAllText($packagePath, $pkgContent, $utf8NoBom)

        if (Test-Path $lockPath) {
            $lockJson = Get-Content $lockPath -Raw | ConvertFrom-Json -AsHashtable
            $lockJson['version'] = $Version
            if ($lockJson.ContainsKey('packages') -and $lockJson['packages'].ContainsKey('')) {
                $lockJson['packages']['']['version'] = $Version
            }
            $lockContent = (($lockJson | ConvertTo-Json -Depth 100) -replace "`r`n", "`n") + "`n"
            [System.IO.File]::WriteAllText($lockPath, $lockContent, $utf8NoBom)
        }

        $relPkg = "$pkg/package.json"
        $relLock = "$pkg/package-lock.json"
        git add $relPkg $relLock
        $stagedFiles += $relPkg
        $stagedFiles += $relLock
    }

    $currentStaged = @(git diff --cached --name-only)
    foreach ($file in $currentStaged) {
        if (-not $file) { continue }
        $normalized = $file.Replace('\', '/')
        if ($normalized -notin $stagedFiles) {
            git reset @stagedFiles | Out-Null
            throw "Unrelated file '$file' is already staged; refusing to create a mixed release commit."
        }
    }

    git diff --cached --quiet
    if ($LASTEXITCODE -eq 0) {
        git reset @stagedFiles | Out-Null
        Write-Host "All package versions are already '$Version'; skipping version bump commit."
    } else {
        git commit -m "chore(release): bump version to $Version"
        if ($LASTEXITCODE -ne 0) {
            throw 'Failed to commit the version bump; the tag was not created.'
        }
    }

    git push $Remote HEAD
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to push HEAD to '$Remote'; the tag was not created."
    }

    git tag -a $tag -m "Release $tag"
    git push $Remote $tag
    if ($LASTEXITCODE -ne 0) {
        git tag -d $tag | Out-Null
        throw "Failed to push '$tag'; the local tag was removed."
    }

    Write-Host "Published unified tag '$tag'. GitHub Actions will now build, test, and publish all .NET and NPM packages."
} finally {
    Pop-Location
}