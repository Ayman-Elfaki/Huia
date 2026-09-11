/**
 * Whitelisted, browser-visible user claims for Huia Headless.
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
  expiresAt?: number
}

export interface UserSessionRequired extends UserSession {
  user: UserClaims
  loggedIn: true
}

export interface HuiaTokenResponse {
  tokenType: string
  accessToken: string
  refreshToken: string
  expiresIn: number
}

export interface PhoneLoginStartResponse {
  message: string
}

export interface PhoneLoginVerifyResponse {
  requiresProfileCompletion?: boolean
  provisionalToken?: string
  tokenType?: string
  accessToken?: string
  refreshToken?: string
  expiresIn?: number
}
