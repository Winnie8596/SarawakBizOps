// Minimal hand-rolled fetch wrapper — no axios dependency to keep the
// package surface small and predictable for a starter project.
const API_BASE_URL = import.meta.env.VITE_API_BASE_URL ?? 'http://localhost:5080/api'

export class ApiError extends Error {
  status: number
  constructor(status: number, message: string) {
    super(message)
    this.status = status
  }
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

  const isJson = response.headers.get('content-type')?.includes('application/json')
  const body = isJson ? await response.json() : undefined

  if (!response.ok) {
    const message = (body && typeof body.message === 'string' && body.message) ||
      `Request failed with status ${response.status}`
    throw new ApiError(response.status, message)
  }

  return body as T
}
