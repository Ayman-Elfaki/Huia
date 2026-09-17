#!/usr/bin/env bash
set -euo pipefail

target="${1:-}"
version="${2:-}"
remote="${3:-origin}"
force=false

if [[ "$target" == '--force' || "$target" == '-f' ]]; then
  force=true
  target="${2:-}"
  version="${3:-}"
  remote="${4:-origin}"
elif [[ "$version" == '--force' || "$version" == '-f' ]]; then
  force=true
  version="${3:-}"
  remote="${4:-origin}"
elif [[ "$remote" == '--force' || "$remote" == '-f' ]]; then
  force=true
  remote="${4:-origin}"
fi

declare -A prefixes=(
  [dotnet]='v'
  [nuxt-oidc]='nuxt-v'
  [nuxt-headless]='nuxt-headless-v'
  [next-oidc]='next-oidc-v'
  [next-headless]='next-headless-v'
  [core]='core-v'
)

declare -A packages=(
  [dotnet]=''
  [nuxt-oidc]='src/javascript/nuxt/nuxt-huia-oidc/package.json'
  [nuxt-headless]='src/javascript/nuxt/nuxt-huia-headless/package.json'
  [next-oidc]='src/javascript/next/next-huia-oidc/package.json'
  [next-headless]='src/javascript/next/next-huia-headless/package.json'
  [core]='src/javascript/shared/huia-auth-core/package.json'
)

declare -A locks=(
  [dotnet]=''
  [nuxt-oidc]='src/javascript/nuxt/nuxt-huia-oidc/package-lock.json'
  [nuxt-headless]='src/javascript/nuxt/nuxt-huia-headless/package-lock.json'
  [next-oidc]='src/javascript/next/next-huia-oidc/package-lock.json'
  [next-headless]='src/javascript/next/next-huia-headless/package-lock.json'
  [core]='src/javascript/shared/huia-auth-core/package-lock.json'
)

if [[ -z "$target" ]]; then
  printf '%s\n' 'Select the package to publish:'
  select choice in dotnet nuxt-oidc nuxt-headless next-oidc next-headless core; do
    if [[ -n "${choice:-}" ]]; then
      target="$choice"
      break
    fi
    printf '%s\n' 'Please choose a number from the list.' >&2
  done
fi

if [[ -z "${prefixes[$target]+x}" ]]; then
  printf "Unknown target '%s'. Choose dotnet, nuxt-oidc, nuxt-headless, next-oidc, next-headless, or core.\n" "$target" >&2
  exit 1
fi

if [[ -z "$version" ]]; then
  read -r -p 'Version (for example 1.2.3 or 1.2.3-alpha.1): ' version
fi

if [[ ! "$version" =~ ^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(-[0-9A-Za-z-]+(\.[0-9A-Za-z-]+)*)?(\+[0-9A-Za-z-]+(\.[0-9A-Za-z-]+)*)?$ ]]; then
  printf "Invalid version '%s'. Use semantic versioning such as 1.2.3 or 1.2.3-alpha.1.\n" "$version" >&2
  exit 1
fi

script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
cd "$script_dir/.."

git rev-parse --show-toplevel >/dev/null
git ls-remote --exit-code "$remote" >/dev/null

tag="${prefixes[$target]}$version"
if [[ "$force" == true ]]; then
  if git rev-parse --verify --quiet "refs/tags/$tag" >/dev/null; then
    git tag -d "$tag" >/dev/null
  fi
  if git ls-remote --exit-code --tags "$remote" "refs/tags/$tag" >/dev/null; then
    git push "$remote" ":refs/tags/$tag"
  fi
else
  if git rev-parse --verify --quiet "refs/tags/$tag" >/dev/null; then
    printf "Tag '%s' already exists locally.\n" "$tag" >&2
    exit 1
  fi
  if git ls-remote --exit-code --tags "$remote" "refs/tags/$tag" >/dev/null; then
    printf "Tag '%s' already exists on '%s'.\n" "$tag" "$remote" >&2
    exit 1
  fi
fi

package_path="${packages[$target]}"
if [[ -n "$package_path" ]]; then
  lock_path="${locks[$target]}"
  VERSION="$version" PACKAGE_PATH="$package_path" LOCK_PATH="$lock_path" node <<'NODE'
const fs = require('fs');
const packagePath = process.env.PACKAGE_PATH;
const packageJson = JSON.parse(fs.readFileSync(packagePath, 'utf8'));
packageJson.version = process.env.VERSION;
fs.writeFileSync(packagePath, `${JSON.stringify(packageJson, null, 2)}\n`);
const lockPath = process.env.LOCK_PATH;
const lockJson = JSON.parse(fs.readFileSync(lockPath, 'utf8'));
lockJson.version = process.env.VERSION;
lockJson.packages[''].version = process.env.VERSION;
fs.writeFileSync(lockPath, `${JSON.stringify(lockJson, null, 2)}\n`);
NODE

  git add "$package_path" "$lock_path"
  mapfile -t staged_files < <(git diff --cached --name-only)
  if (( ${#staged_files[@]} != 2 )) || [[ " ${staged_files[*]} " != *" $package_path "* ]] || [[ " ${staged_files[*]} " != *" $lock_path "* ]]; then
    git reset "$package_path" "$lock_path" >/dev/null
    printf '%s\n' 'Unrelated files are already staged; refusing to create a mixed release commit.' >&2
    exit 1
  fi
  if git diff --cached --quiet; then
    git reset "$package_path" "$lock_path" >/dev/null
    if [[ "$force" != true ]]; then
      printf "Package version is already '%s' in '%s'.\n" "$version" "$package_path" >&2
      exit 1
    fi
  else
    git commit -m "chore($target): bump version to $version"
    if ! git push "$remote" HEAD; then
      printf "Failed to push the version bump to '%s'; the tag was not created.\n" "$remote" >&2
      exit 1
    fi
  fi
fi

git tag -a "$tag" -m "Release $tag"
if ! git push "$remote" "$tag"; then
  git tag -d "$tag" >/dev/null
  printf "Failed to push '%s'; the local tag was removed.\n" "$tag" >&2
  exit 1
fi

printf "Published tag '%s'. GitHub Actions will now handle the release.\n" "$tag"