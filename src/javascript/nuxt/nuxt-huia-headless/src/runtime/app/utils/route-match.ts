/**
 * Matches `path` against a glob-ish exclude pattern: `**` matches any depth (including zero
 * segments), `*` matches exactly one path segment, everything else is a literal. `/auth/**` matches
 * `/auth`, `/auth/login` and anything else under `/auth`; `/about` matches only `/about`.
 */
export function matchesPattern(path: string, pattern: string): boolean {
  const regexSource = pattern
    .split('/')
    .map(segment =>
      segment === '**'
        ? '.*'
        : segment.replace(/[.+?^${}()|[\]\\]/g, '\\$&').replace(/\*/g, '[^/]*'),
    )
    .join('/')
    // `/auth/**` should also match the bare `/auth` prefix, not just `/auth/<something>`.
    .replace(/\/\.\*$/, '(?:/.*)?')

  return new RegExp(`^${regexSource}$`).test(path)
}

/** Whether `path` matches any pattern in `exclude`. */
export function isExcluded(path: string, exclude: string[]): boolean {
  return exclude.some(pattern => matchesPattern(path, pattern))
}
