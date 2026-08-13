// Mirrors the response/request record shapes in src/Huia/Endpoints/Manage/*.cs, proxied through
// server/api/manage/[...path].ts.

export interface ManageInfoResponse {
  email: string
  isEmailConfirmed: boolean
  firstName: string | null
  lastName: string | null
}

export interface UpdateInfoRequest {
  newEmail?: string | null
  newPassword?: string | null
  oldPassword?: string | null
  firstName?: string | null
  lastName?: string | null
}

export interface TwoFactorResponse {
  isTwoFactorEnabled: boolean
  sharedKey: string | null
  recoveryCodes: string[] | null
  recoveryCodesLeft: number
}

export interface TwoFactorRequest {
  enable?: boolean | null
  twoFactorCode?: string | null
  resetSharedKey?: boolean | null
  resetRecoveryCodes?: boolean | null
}

export interface ExternalLoginResponse {
  loginProvider: string
  providerDisplayName: string | null
  providerKey: string
}

export interface ExternalLoginsResponse {
  logins: ExternalLoginResponse[]
  hasPassword: boolean
}
