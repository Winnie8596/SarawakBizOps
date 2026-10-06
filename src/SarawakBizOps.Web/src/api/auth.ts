import { apiFetch } from './client'
import type { CurrentUser, LoginResult } from '../types'

// The session cookie (the refresh token) is HttpOnly and scoped to /api/auth: these three calls are the only ones
// that send or receive it. `include` lets the Vite dev server on another port do so too; behind nginx it is the
// same origin anyway.

export function login(email: string, password: string): Promise<LoginResult> {
  return apiFetch<LoginResult>('/auth/login', {
    method: 'POST',
    credentials: 'include',
    body: JSON.stringify({ email, password })
  })
}

/** A new access token for the session in the cookie, or a 401 ApiError when there is none (or it has ended). */
export function refreshSession(): Promise<LoginResult> {
  return apiFetch<LoginResult>('/auth/refresh', { method: 'POST', credentials: 'include' })
}

export function logout(): Promise<void> {
  return apiFetch<void>('/auth/logout', { method: 'POST', credentials: 'include' })
}

// Not called by the app: restoring the session on load (refreshSession) already re-checks the user, and a
// deactivated user's refresh fails. Kept for scripts and debugging.
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
