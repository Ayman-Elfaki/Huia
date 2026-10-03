#!/usr/bin/env bash
set -euo pipefail

version=""
remote="origin"
force=false

for arg in "$@"; do
  case "$arg" in
    --force|-f)
      force=true
      ;;
    *)
      if [[ -z "$version" ]]; then
        version="$arg"
      elif [[ "$remote" == "origin" ]]; then
        remote="$arg"
      fi
      ;;
  esac
done

if [[ -z "$version" ]]; then
  read -r -p 'Version to release (for example 1.0.0-alpha.16): ' version
fi

if [[ ! "$version" =~ ^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(-[0-9A-Za-z-]+(\.[0-9A-Za-z-]+)*)?(\+[0-9A-Za-z-]+(\.[0-9A-Za-z-]+)*)?$ ]]; then
  printf "Invalid version '%s'. Use semantic versioning such as 1.0.0-alpha.16 or 1.0.0.\n" "$version" >&2
  exit 1
fi

script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
cd "$script_dir/.."

git rev-parse --show-toplevel >/dev/null
git ls-remote --exit-code "$remote" >/dev/null

tag="v$version"

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

packages=(
  "src/javascript/shared/huia-auth-core"
  "src/javascript/next/next-huia-oidc"
  "src/javascript/next/next-huia-headless"
  "src/javascript/nuxt/nuxt-huia-oidc"
  "src/javascript/nuxt/nuxt-huia-headless"
)

staged_files=()

for pkg in "${packages[@]}"; do
  pkg_json="$pkg/package.json"
  lock_json="$pkg/package-lock.json"

  VERSION="$version" PACKAGE_PATH="$pkg_json" LOCK_PATH="$lock_json" node <<'NODE'
const fs = require('fs');
const pkgPath = process.env.PACKAGE_PATH;
const pkg = JSON.parse(fs.readFileSync(pkgPath, 'utf8'));
pkg.version = process.env.VERSION;
fs.writeFileSync(pkgPath, `${JSON.stringify(pkg, null, 2)}\n`);

const lockPath = process.env.LOCK_PATH;
if (fs.existsSync(lockPath)) {
  const lock = JSON.parse(fs.readFileSync(lockPath, 'utf8'));
  lock.version = process.env.VERSION;
  if (lock.packages && lock.packages['']) {
    lock.packages[''].version = process.env.VERSION;
  }
  fs.writeFileSync(lockPath, `${JSON.stringify(lock, null, 2)}\n`);
}
NODE

  git add "$pkg_json" "$lock_json"
  staged_files+=("$pkg_json" "$lock_json")
done

mapfile -t current_staged < <(git diff --cached --name-only)
for file in "${current_staged[@]}"; do
  matched=false
  for expected in "${staged_files[@]}"; do
    if [[ "$file" == "$expected" ]]; then
      matched=true
      break
    fi
  done
  if [[ "$matched" == false ]]; then
    git reset "${staged_files[@]}" >/dev/null
    printf "Unrelated file '%s' is already staged; refusing to create a mixed release commit.\n" "$file" >&2
    exit 1
  fi
done

if git diff --cached --quiet; then
  git reset "${staged_files[@]}" >/dev/null
  if [[ "$force" != true ]]; then
    printf "All package versions are already '%s'.\n" "$version" >&2
    exit 1
  fi
else
  git commit -m "chore(release): bump version to $version"
  if ! git push "$remote" HEAD; then
    printf "Failed to push the version bump to '%s'; the tag was not created.\n" "$remote" >&2
    exit 1
  fi
fi

git tag -a "$tag" -m "Release $tag"
if ! git push "$remote" "$tag"; then
  git tag -d "$tag" >/dev/null
  printf "Failed to push '%s'; the local tag was removed.\n" "$tag" >&2
  exit 1
fi

printf "Published unified tag '%s'. GitHub Actions will now build, test, and publish all .NET and NPM packages.\n" "$tag"