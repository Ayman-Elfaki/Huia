/**
 * Whitelisted, browser-visible user claims — sourced from Huia.Headless's `identity/me` endpoint
 * (the bearer token itself is opaque, so claims can't be read off it directly). Declared as an
 * `interface` so a consuming app can `declare module '#huia-headless-auth'` and merge extra claims
 * into it project-wide.
 */
export interface UserClaims {
  sub: string
  email?: string
  firstName?: string
  lastName?: string
  roles?: string[]
  [key: string]: unknown
}

export interface UserSession {
  user?: UserClaims
  loggedIn?: boolean
  /** ms epoch — when the current access token expires (already accounts for a completed refresh). */
  expiresAt?: number
}

export interface UserSessionRequired extends UserSession {
  user: UserClaims
  loggedIn: true
}

/**
 * Server-only. Declared `interface` for the same consumer-augmentation reason as {@link UserClaims}.
 */
export interface SecureSessionData {
  [key: string]: unknown
}
