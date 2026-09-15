/**
 * Whitelisted, browser-visible user claims. Declared as an `interface` so a consuming app can
 * `declare module '#huia-auth'` and merge extra claims into it project-wide.
 */
export interface UserClaims {
  sub: string
  name?: string
  email?: string
  preferred_username?: string
  given_name?: string
  family_name?: string
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
 * Server-only. The full id_token claim set held in Nitro Storage. Declared `interface` for the same
 * consumer-augmentation reason as {@link UserClaims}.
 */
export interface SecureSessionData {
  [key: string]: unknown
}
