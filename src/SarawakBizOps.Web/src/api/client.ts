// Minimal hand-rolled fetch wrapper — no axios dependency to keep the
// package surface small and predictable for a starter project.
const API_BASE_URL = import.meta.env.VITE_API_BASE_URL ?? 'http://localhost:5080/api'

export class ApiError extends Error {
  status: number
  /** Per-field validation messages when the API returned a 400 with `errors`. */
  fieldErrors?: Record<string, string[]>
  constructor(status: number, message: string, fieldErrors?: Record<string, string[]>) {
    super(message)
    this.status = status
    this.fieldErrors = fieldErrors
  }
}

// The API returns RFC 7807 ProblemDetails for every 4xx/5xx response.
interface ProblemDetails {
  title?: string
  detail?: string
  errors?: Record<string, string[]>
}

function messageFromProblem(status: number, problem: ProblemDetails | undefined): string {
  if (problem?.detail) return problem.detail

  const firstFieldError = problem?.errors && Object.values(problem.errors).flat()[0]
  if (firstFieldError) return firstFieldError

  switch (status) {
    case 401: return 'Your session has expired. Please sign in again.'
    case 403: return 'You do not have permission to do that.'
    case 404: return 'That record could not be found.'
    case 409: return 'This record was changed by someone else. Please refresh and try again.'
    default: return problem?.title || `Request failed with status ${status}`
  }
}

// AuthProvider registers this so an expired/invalid token clears the session;
// ProtectedRoute then redirects to /login.
let onUnauthorized: (() => void) | null = null
export function setUnauthorizedHandler(handler: (() => void) | null) {
  onUnauthorized = handler
}

interface RequestOptions extends RequestInit {
  token?: string | null
}

export async function apiFetch<T>(path: string, options: RequestOptions = {}): Promise<T> {
  const { token, headers, ...rest } = options

  const response = await fetch(`${API_BASE_URL}${path}`, {
    ...rest,
    headers: {
      'Content-Type': 'application/json',
      ...(token ? { Authorization: `Bearer ${token}` } : {}),
      ...headers
    }
  })

  if (response.status === 204) {
    return undefined as T
  }

  const isJson = response.headers.get('content-type')?.includes('json')
  const body = isJson ? await response.json() : undefined

  if (!response.ok) {
    // A 401 on a call that carried a token means the session is no longer
    // valid. A 401 without a token (bad login) is just an error to display.
    if (response.status === 401 && token) {
      onUnauthorized?.()
    }
    throw new ApiError(response.status, messageFromProblem(response.status, body), body?.errors)
  }

  return body as T
}
