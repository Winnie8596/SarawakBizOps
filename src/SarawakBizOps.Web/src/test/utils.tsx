import { render } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { vi } from 'vitest'
import App from '../App'

// The session the stubbed POST /auth/refresh hands out, as the server would from the browser's cookie.
let session: Record<string, unknown> | null = null

/** Gives the browser a signed-in session: on load the app restores it through POST /auth/refresh. */
export function signInAs(role: string, fullName = 'Test User') {
  session = {
    token: 'test-token',
    fullName,
    email: `${role.toLowerCase()}@test.local`,
    userId: `${role}-id`,
    roles: [role],
    expiresAtUtc: new Date(Date.now() + 15 * 60 * 1000).toISOString()
  }
}

/** Called after each test (setup.ts): no session leaks into the next one. */
export function resetSession() {
  session = null
}

export interface StubbedResponse {
  status?: number
  body?: unknown
}

export interface StubbedRequest {
  url: URL
  body: unknown
}

type Route = StubbedResponse | ((request: StubbedRequest) => StubbedResponse)

/**
 * Replaces fetch with a stub keyed by "METHOD /path" (the /api prefix and the query string are ignored
 * in the key). Unknown calls answer with a 404 ProblemDetails, so a forgotten stub fails loudly.
 * Returns the mock so tests can assert on the exact calls the UI made.
 */
export function stubApi(routes: Record<string, Route>) {
  const fetchMock = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    const url = new URL(String(input))
    const method = (init?.method ?? 'GET').toUpperCase()
    const key = `${method} ${url.pathname.replace(/^\/api/, '')}`
    // Unless a test says otherwise, the refresh call answers like the server: the signed-in session, or a 401.
    const route = routes[key] ?? (key === 'POST /auth/refresh'
      ? (session ? { body: session } : { status: 401, body: { title: 'Unauthorized', detail: 'Your session has ended. Please sign in again.' } })
      : undefined)

    if (!route) {
      return respond({ status: 404, body: { title: 'Not found', detail: `No stub for ${key}` } })
    }

    const body = typeof init?.body === 'string' ? JSON.parse(init.body) : undefined
    return respond(typeof route === 'function' ? route({ url, body }) : route)
  })

  vi.stubGlobal('fetch', fetchMock)
  return fetchMock
}

async function respond({ status = 200, body }: StubbedResponse): Promise<Response> {
  if (body === undefined) return new Response(null, { status })
  // A file served through the API (e.g. the signature image) comes back as its own bytes, not JSON.
  // Node's Response cannot read a jsdom Blob, so it gets the bytes.
  if (body instanceof Blob) {
    return new Response(await body.arrayBuffer(), { status, headers: { 'content-type': body.type } })
  }
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'content-type': status >= 400 ? 'application/problem+json' : 'application/json' }
  })
}

/** Renders the whole app (real routes, guards, layout) at a path, as main.tsx does inside a router. */
export function renderApp(path: string) {
  return render(
    <MemoryRouter initialEntries={[path]}>
      <App />
    </MemoryRouter>
  )
}

/** The calls the UI made to one endpoint, e.g. calledWith(fetchMock, 'POST', '/service-requests/1/approve'). */
export function callsTo(fetchMock: ReturnType<typeof stubApi>, method: string, path: string) {
  return fetchMock.mock.calls.filter(([input, init]) => {
    const url = new URL(String(input))
    return (init?.method ?? 'GET').toUpperCase() === method && url.pathname === `/api${path}`
  })
}
