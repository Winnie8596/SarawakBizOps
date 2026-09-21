import { createContext, useContext, useEffect, useMemo, useState, type ReactNode } from 'react'
import { login as loginRequest } from '../api/auth'
import type { LoginResult } from '../types'

interface AuthState {
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
  login: (email: string, password: string) => Promise<void>
  logout: () => void
  hasRole: (...roles: string[]) => boolean
}

const STORAGE_KEY = 'sarawakbizops.auth'

const AuthContext = createContext<AuthContextValue | undefined>(undefined)

function loadStoredAuth(): AuthState | null {
  const raw = localStorage.getItem(STORAGE_KEY)
  if (!raw) return null

  try {
    const parsed = JSON.parse(raw) as AuthState
    if (new Date(parsed.expiresAtUtc) <= new Date()) {
      localStorage.removeItem(STORAGE_KEY)
      return null
    }
    return parsed
  } catch {
    return null
  }
}

export function AuthProvider({ children }: { children: ReactNode }) {
  const [auth, setAuth] = useState<AuthState | null>(() => loadStoredAuth())

  useEffect(() => {
    if (auth) {
      localStorage.setItem(STORAGE_KEY, JSON.stringify(auth))
    } else {
      localStorage.removeItem(STORAGE_KEY)
    }
  }, [auth])

  const value = useMemo<AuthContextValue>(() => ({
    auth,
    isAuthenticated: auth !== null,
    login: async (email: string, password: string) => {
      const result: LoginResult = await loginRequest(email, password)
      setAuth({
        token: result.token,
        fullName: result.fullName,
        email: result.email,
        userId: result.userId,
        roles: result.roles,
        expiresAtUtc: result.expiresAtUtc
      })
    },
    logout: () => setAuth(null),
    hasRole: (...roles: string[]) => auth !== null && roles.some(r => auth.roles.includes(r))
  }), [auth])

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>
}

export function useAuth(): AuthContextValue {
  const ctx = useContext(AuthContext)
  if (!ctx) {
    throw new Error('useAuth must be used within an AuthProvider')
  }
  return ctx
}
