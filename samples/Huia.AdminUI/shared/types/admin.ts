// Mirrors the response/request record shapes in src/Huia/Endpoints/Admin/*.cs and
// samples/Huia.TodoApi's MapHuiaAdminEndpoints() (proxied through nuxt-api-party's huiaAdmin endpoint).

/**
 * Mirrors MR.AspNetCore.Pagination's KeysetPaginationResult<T> - what every admin list endpoint returns when
 * Huia is configured with an EF Core store (see WithEntityFrameworkStores). There's no cursor token in the
 * response itself: the next/previous page is requested by sending the id of the last/first item already on
 * screen as the `after`/`before` query param (see useAdminList.ts).
 */
export interface KeysetPage<T> {
  data: T[]
  totalCount: number
  pageSize: number
  hasPrevious: boolean
  hasNext: boolean
}

export type ApplicationKind = 'spa' | 'native' | 'web' | 'm2m' | 'device'

export interface ApplicationResponse {
  id: string
  clientId: string
  displayName: string | null
  kind: string
  applicationType: string | null
  clientType: string | null
  consentType: string | null
  redirectUris: string[]
  postLogoutRedirectUris: string[]
  permissions: string[]
  requirements: string[]
  accessTokenLifetime: string | null
  identityTokenLifetime: string | null
  refreshTokenLifetime: string | null
}

export interface ApplicationRequest {
  kind: ApplicationKind
  clientId: string
  displayName?: string | null
  clientSecret?: string | null
  requiresPkce: boolean
  requireConsent: boolean
  redirectUris?: string[]
  postLogoutRedirectUris?: string[]
  scopes?: string[]
}

export interface ScopeResponse {
  id: string
  name: string
  displayName: string | null
  description: string | null
  resources: string[]
}

export interface ScopeRequest {
  name: string
  displayName?: string | null
  description?: string | null
  resources?: string[]
}

export interface AdminExternalLoginResponse {
  loginProvider: string
  providerDisplayName: string | null
}

export interface UserResponse {
  id: string
  email: string | null
  userName: string | null
  firstName: string | null
  lastName: string | null
  picture: string | null
  emailConfirmed: boolean
  phoneNumber: string | null
  isLockedOut: boolean
  twoFactorEnabled: boolean
  hasPassword: boolean
  roles: string[]
  authenticationMethods: string[]
  externalLogins: AdminExternalLoginResponse[]
}

/** Either `email`+`password` (a password-based account) or `phoneNumber` (a passwordless phone account, in
 * international format e.g. `+15551234567`) must be supplied, never both. */
export interface CreateUserRequest {
  email?: string | null
  password?: string | null
  phoneNumber?: string | null
  firstName?: string | null
  lastName?: string | null
  emailConfirmed: boolean
}

export interface UpdateUserRequest {
  email?: string | null
  firstName?: string | null
  lastName?: string | null
  emailConfirmed: boolean
}

export interface RoleResponse {
  id: string
  name: string
}

export interface RoleMemberResponse {
  id: string
  email: string
}

export interface AuthenticationFlowsResponse {
  emailAndPasswordFlowEnabled: boolean
  passwordlessFlowEnabled: boolean
}

export interface AuthorizationResponse {
  id: string
  applicationId: string | null
  subject: string | null
  status: string | null
  type: string | null
  creationDate: string | null
  scopes: string[]
}
