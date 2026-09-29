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

async function send(path: string, options: RequestOptions): Promise<Response> {
  const { token, headers, body, ...rest } = options

  const response = await fetch(`${API_BASE_URL}${path}`, {
    ...rest,
    body,
    headers: {
      // A FormData body (file uploads) must let the browser set multipart/form-data with its boundary.
      ...(body instanceof FormData ? {} : { 'Content-Type': 'application/json' }),
      ...(token ? { Authorization: `Bearer ${token}` } : {}),
      ...headers
    }
  })

  if (!response.ok) {
    const isJson = response.headers.get('content-type')?.includes('json')
    const problem = isJson ? await response.json() : undefined
    // A 401 on a call that carried a token means the session is no longer
    // valid. A 401 without a token (bad login) is just an error to display.
    if (response.status === 401 && token) {
      onUnauthorized?.()
    }
    throw new ApiError(response.status, messageFromProblem(response.status, problem), problem?.errors)
  }

  return response
}

export async function apiFetch<T>(path: string, options: RequestOptions = {}): Promise<T> {
  const response = await send(path, options)

  if (response.status === 204) {
    return undefined as T
  }

  const isJson = response.headers.get('content-type')?.includes('json')
  return (isJson ? await response.json() : undefined) as T
}

interface UploadOptions {
  token?: string | null
  body: FormData
  /** Called with the fraction of the request body sent so far (0–1). */
  onProgress?: (fraction: number) => void
}

/**
 * A multipart upload that reports progress (Phase 9's job photos). `fetch` cannot report upload progress, so
 * this one call uses XMLHttpRequest; errors come back as the same ApiError as every other call.
 */
export function apiUpload<T>(path: string, { token, body, onProgress }: UploadOptions): Promise<T> {
  return new Promise<T>((resolve, reject) => {
    const xhr = new XMLHttpRequest()
    xhr.open('POST', `${API_BASE_URL}${path}`)
    // No Content-Type: the browser sets multipart/form-data with its boundary.
    if (token) xhr.setRequestHeader('Authorization', `Bearer ${token}`)

    xhr.upload.onprogress = event => {
      if (event.lengthComputable) onProgress?.(event.loaded / event.total)
    }

    xhr.onload = () => {
      const isJson = xhr.getResponseHeader('content-type')?.includes('json')
      let parsed: unknown
      try {
        parsed = isJson && xhr.responseText ? JSON.parse(xhr.responseText) : undefined
      } catch {
        parsed = undefined
      }

      if (xhr.status >= 200 && xhr.status < 300) {
        resolve(parsed as T)
        return
      }

      const problem = parsed as ProblemDetails | undefined
      if (xhr.status === 401 && token) onUnauthorized?.()
      reject(new ApiError(xhr.status, messageFromProblem(xhr.status, problem), problem?.errors))
    }

    // Network failure (no response at all): status 0, so it is never mistaken for a 409.
    xhr.onerror = () => reject(new ApiError(0, 'The upload did not reach the server. Check the connection and try again.'))

    xhr.send(body)
  })
}

/** For files served through the API (e.g. the signature image): the bearer token rides along, which a plain
 *  `<img src>` could not do, so the caller turns the Blob into an object URL. */
export async function apiFetchBlob(path: string, options: RequestOptions = {}): Promise<Blob> {
  const response = await send(path, options)
  return response.blob()
}
