// OpenIddict's role claim type is "role" (singular) — a token with more than one role serializes it as a
// JSON array under that same key rather than pluralizing the key itself (see nuxt.config.ts's
// optionalClaims), so a decoded claim value can be a single string, an array, or absent.
export function normalizeRoles(role: unknown): string[] {
  if (typeof role === 'string') return [role]
  if (Array.isArray(role)) return role.filter((r): r is string => typeof r === 'string')
  return []
}
