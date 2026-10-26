import { api, resetAntiforgery } from './client'

export interface Me {
  id: string
  email: string
  /** The user's own platform staff roles (empty for customers). */
  platformRoles: string[]
}

export interface Session {
  id: string
  createdAt: string
  lastSeenAt: string
  ipAddress: string | null
  userAgent: string | null
  current: boolean
}

export interface Provisioning {
  sharedKey: string
  otpauthUri: string
}

export type LoginNext = 'enrol' | 'verify'

/** Calls that change who is signed in; the anti-forgery token is renewed afterwards. */
async function changesSignIn<T>(call: Promise<T>): Promise<T> {
  const result = await call
  resetAntiforgery()
  return result
}

export const authApi = {
  me: () => api<Me>('GET', '/me'),
  sessions: () => api<Session[]>('GET', '/me/sessions'),
  revokeSession: (id: string) => api('DELETE', `/me/sessions/${id}`),
  register: (email: string, password: string) => api('POST', '/auth/register', { email, password }),
  confirmEmail: (userId: string, token: string) =>
    api('POST', '/auth/confirm-email', { userId, token }),
  login: (email: string, password: string) =>
    changesSignIn(api<{ next: LoginNext }>('POST', '/auth/login', { email, password })),
  startEnrolment: () => api<Provisioning>('POST', '/auth/mfa/enrol/start'),
  confirmEnrolment: (code: string) =>
    changesSignIn(api<{ recoveryCodes: string[] }>('POST', '/auth/mfa/enrol/confirm', { code })),
  verify: (code: string | null, recoveryCode: string | null) =>
    changesSignIn(api('POST', '/auth/mfa/verify', recoveryCode ? { recoveryCode } : { code })),
  stepUp: (code: string) => api('POST', '/auth/step-up', { code }),
  provisioning: () => api<Provisioning>('POST', '/auth/mfa/provisioning'),
  logout: () => changesSignIn(api('POST', '/auth/logout')),
  forgotPassword: (email: string) => api('POST', '/auth/password/forgot', { email }),
  resetPassword: (userId: string, token: string, newPassword: string) =>
    api('POST', '/auth/password/reset', { userId, token, newPassword }),
}
