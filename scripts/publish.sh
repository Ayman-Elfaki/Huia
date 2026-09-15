#!/usr/bin/env bash
set -euo pipefail

target="${1:-}"
version="${2:-}"
remote="${3:-origin}"

declare -A prefixes=(
  [dotnet]='v'
  [nuxt-oidc]='nuxt-v'
  [nuxt-headless]='nuxt-headless-v'
  [next-oidc]='next-oidc-v'
  [next-headless]='next-headless-v'
  [core]='core-v'
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
if git rev-parse --verify --quiet "refs/tags/$tag" >/dev/null; then
  printf "Tag '%s' already exists locally.\n" "$tag" >&2
  exit 1
fi
if git ls-remote --exit-code --tags "$remote" "refs/tags/$tag" >/dev/null; then
  printf "Tag '%s' already exists on '%s'.\n" "$tag" "$remote" >&2
  exit 1
fi

git tag -a "$tag" -m "Release $tag"
if ! git push "$remote" "$tag"; then
  git tag -d "$tag" >/dev/null
  printf "Failed to push '%s'; the local tag was removed.\n" "$tag" >&2
  exit 1
fi

printf "Published tag '%s'. GitHub Actions will now handle the release.\n" "$tag"