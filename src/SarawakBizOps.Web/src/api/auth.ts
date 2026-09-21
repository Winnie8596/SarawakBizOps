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
