// Mirrors the response/request record shapes in src/Huia/Endpoints/Manage/*.cs, proxied through
// server/api/manage/[...path].ts.

export interface ManageInfoResponse {
  email: string | null
  isEmailConfirmed: boolean
  phoneNumber: string | null
  isPhoneNumberConfirmed: boolean
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

/** Step 1 of the OTP-verified phone number change - sends a code to `newPhoneNumber` (must already be
 * fully E.164-qualified, e.g. `+15551234567`). See RequestPhoneChangeResponse/VerifyPhoneChangeRequest. */
export interface RequestPhoneChangeRequest {
  newPhoneNumber: string
}

export interface RequestPhoneChangeResponse {
  maskedPhoneNumber: string
}

/** Step 2 - resends the same number alongside the code the user received, since the token is bound to that
 * specific number rather than tracked via server-side pending state. */
export interface VerifyPhoneChangeRequest {
  newPhoneNumber: string
  code: string
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
