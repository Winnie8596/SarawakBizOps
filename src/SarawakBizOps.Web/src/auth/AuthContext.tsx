import { createContext, useCallback, useContext, useEffect, useMemo, useRef, useState, type ReactNode } from 'react'
import { login as loginRequest, logout as logoutRequest, refreshSession } from '../api/auth'
import { setAccessToken, setAccessTokenRefresher, setUnauthorizedHandler } from '../api/client'
import type { LoginResult } from '../types'

interface AuthState {
  /** The access token at sign-in. The API client always sends the newest one (see setAccessToken). */
  token: string
  fullName: string
  email: string
  userId: string
  roles: string[]
  expiresAtUtc: string
}

interface AuthContextValue {
  auth: AuthState | null
  isAuthenticated: boolean
  /** True while the app asks the server, on first load, whether the browser still has a session. */
  restoring: boolean
  login: (email: string, password: string) => Promise<void>
  logout: () => void
  hasRole: (...roles: string[]) => boolean
}

// Before refresh tokens the whole session, access token included, was kept in localStorage under this key.
const LEGACY_STORAGE_KEY = 'sarawakbizops.auth'

// Renew this long before the access token expires, so a request rarely meets an expired one.
const REFRESH_AHEAD_MS = 60_000

const AuthContext = createContext<AuthContextValue | undefined>(undefined)

function sameUser(a: AuthState | null, b: LoginResult): boolean {
  return a !== null && a.userId === b.userId && a.fullName === b.fullName && a.email === b.email
    && a.roles.join() === b.roles.join()
}

function toState(result: LoginResult): AuthState {
  return {
    token: result.token,
    fullName: result.fullName,
    email: result.email,
    userId: result.userId,
    roles: result.roles,
    expiresAtUtc: result.expiresAtUtc
  }
}

/**
 * The access token lives only in memory, never in localStorage, so a script injected into the page cannot carry
 * it off for later. What keeps a sign-in across reloads is the refresh token, in an HttpOnly cookie the page cannot
 * read at all: on load, and shortly before each access token expires, the app exchanges it for a new one.
 */
export function AuthProvider({ children }: { children: ReactNode }) {
  const [auth, setAuth] = useState<AuthState | null>(null)
  const [restoring, setRestoring] = useState(true)
  // When the current access token expires. Kept apart from `auth`, so a refresh re-renders nothing but this
  // provider: `auth` (and every page that loads data from it) changes only when the user does.
  const [expiresAtUtc, setExpiresAtUtc] = useState<string | null>(null)

  const startSession = useCallback((result: LoginResult) => {
    setAccessToken(result.token)
    setExpiresAtUtc(result.expiresAtUtc)
    setAuth(previous => (sameUser(previous, result) ? previous : toState(result)))
  }, [])

  const endSession = useCallback(() => {
    setAccessToken(null)
    setExpiresAtUtc(null)
    setAuth(null)
  }, [])
  // One refresh at a time: the timer, a 401 retry and a second component can all ask at once, and each refresh
  // rotates the cookie.
  const inFlight = useRef<Promise<string | null> | null>(null)

  const refresh = useCallback((): Promise<string | null> => {
    inFlight.current ??= refreshSession()
      .then(result => {
        startSession(result)
        return result.token
      })
      .catch(() => {
        endSession()
        return null
      })
      .finally(() => {
        inFlight.current = null
      })
    return inFlight.current
  }, [startSession, endSession])

  // First load: is there still a session in the cookie? Also drop what older versions left in localStorage.
  useEffect(() => {
    localStorage.removeItem(LEGACY_STORAGE_KEY)
    void refresh().finally(() => setRestoring(false))
  }, [refresh])

  // Renew quietly shortly before the access token expires.
  useEffect(() => {
    if (!expiresAtUtc) return
    const delay = Math.max(5_000, new Date(expiresAtUtc).getTime() - Date.now() - REFRESH_AHEAD_MS)
    const timer = setTimeout(() => void refresh(), delay)
    return () => clearTimeout(timer)
  }, [expiresAtUtc, refresh])

  // A 401 on an API call first tries a refresh (see client.ts); only when that fails does the session end.
  useEffect(() => {
    setAccessTokenRefresher(refresh)
    setUnauthorizedHandler(endSession)
    return () => {
      setAccessTokenRefresher(null)
      setUnauthorizedHandler(null)
    }
  }, [refresh, endSession])

  const value = useMemo<AuthContextValue>(() => ({
    auth,
    isAuthenticated: auth !== null,
    restoring,
    login: async (email: string, password: string) => {
      startSession(await loginRequest(email, password))
    },
    logout: () => {
      // Ends the session on the server and clears the cookie; the page signs out at once either way.
      void logoutRequest().catch(() => {})
      endSession()
    },
    hasRole: (...roles: string[]) => auth !== null && roles.some(r => auth.roles.includes(r))
  }), [auth, restoring, startSession, endSession])

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>
}

export function useAuth(): AuthContextValue {
  const ctx = useContext(AuthContext)
  if (!ctx) {
    throw new Error('useAuth must be used within an AuthProvider')
  }
  return ctx
}
