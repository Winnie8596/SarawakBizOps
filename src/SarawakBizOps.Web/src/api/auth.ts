import { apiFetch } from './client'
import type { CurrentUser, LoginResult } from '../types'

export function login(email: string, password: string): Promise<LoginResult> {
  return apiFetch<LoginResult>('/auth/login', {
    method: 'POST',
    body: JSON.stringify({ email, password })
  })
}

// Not called yet (AuthContext trusts the cached login result for the MVP) —
// kept here for when session validation on app load is added, e.g. to
// detect a token whose user was deactivated server-side since login.
export function fetchCurrentUser(token: string): Promise<CurrentUser> {
  return apiFetch<CurrentUser>('/auth/me', { token })
}

export function changePassword(token: string, currentPassword: string, newPassword: string): Promise<void> {
  return apiFetch<void>('/auth/change-password', {
    method: 'POST',
    token,
    body: JSON.stringify({ currentPassword, newPassword })
  })
}
