import { screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it } from 'vitest'
import { customers } from './fixtures'
import { callsTo, renderApp, signInAs, stubApi } from './utils'

describe('Login', () => {
  it('shows the API problem message when the credentials are wrong', async () => {
    stubApi({
      'POST /auth/login': { status: 401, body: { title: 'Unauthorized', detail: 'Invalid email or password.' } }
    })
    renderApp('/login')

    await userEvent.type(screen.getByLabelText('Password'), 'wrong-password')
    await userEvent.click(screen.getByRole('button', { name: 'Sign in' }))

    expect(await screen.findByText('Invalid email or password.')).toBeInTheDocument()
  })

  it('signs in with the session cookie, keeps the token out of storage and lands on the dashboard', async () => {
    const fetchMock = stubApi({
      'POST /auth/login': {
        body: {
          token: 'jwt',
          expiresAtUtc: new Date(Date.now() + 3_600_000).toISOString(),
          userId: 'm1',
          fullName: 'Jenny Lau',
          email: 'manager@sarawakbizops.local',
          roles: ['Manager']
        }
      },
      'GET /customers': { body: [] },
      'GET /equipment': { body: [] }
    })
    renderApp('/login')

    await userEvent.type(screen.getByLabelText('Password'), 'Demo!2026')
    await userEvent.click(screen.getByRole('button', { name: 'Sign in' }))

    expect(await screen.findByRole('heading', { name: 'Welcome back, Jenny' })).toBeInTheDocument()
    // The access token stays in memory; the refresh token is an HttpOnly cookie the page cannot read.
    expect(localStorage.length).toBe(0)
    expect(sessionStorage.length).toBe(0)
    expect(callsTo(fetchMock, 'POST', '/auth/login')[0][1]?.credentials).toBe('include')
  })

  it('restores a signed-in session from the cookie on load, without asking for the password', async () => {
    signInAs('Manager', 'Jenny Lau')
    stubApi({ 'GET /customers': { body: [] }, 'GET /equipment': { body: [] } })
    renderApp('/')

    expect(await screen.findByRole('heading', { name: 'Welcome back, Jenny' })).toBeInTheDocument()
  })

  it('sends a visitor without a session to the login page', async () => {
    stubApi({})
    renderApp('/customers')

    expect(await screen.findByRole('button', { name: 'Sign in' })).toBeInTheDocument()
  })

  it('removes a session that an older version left in localStorage', async () => {
    localStorage.setItem('sarawakbizops.auth', JSON.stringify({ token: 'old-token', roles: ['Admin'] }))
    stubApi({})
    renderApp('/login')

    await waitFor(() => expect(localStorage.getItem('sarawakbizops.auth')).toBeNull())
  })

  it('renews an expired access token once and repeats the call', async () => {
    signInAs('Manager', 'Jenny Lau')
    let customerCalls = 0
    const fetchMock = stubApi({
      'GET /customers': () => (++customerCalls === 1 ? { status: 401 } : { body: customers })
    })
    renderApp('/customers')

    expect(await screen.findByText(customers[0].companyName)).toBeInTheDocument()
    expect(customerCalls).toBe(2)
    // One refresh on load, one for the 401; still signed in.
    expect(callsTo(fetchMock, 'POST', '/auth/refresh')).toHaveLength(2)
    expect(screen.queryByRole('button', { name: 'Sign in' })).not.toBeInTheDocument()
  })

  it('attaches the access token itself, and never to the sign-in and session calls', async () => {
    signInAs('Manager', 'Jenny Lau')
    let refreshes = 0
    const fetchMock = stubApi({
      'POST /auth/refresh': () => ({
        body: {
          token: `token-${++refreshes}`,
          expiresAtUtc: new Date(Date.now() + 3_600_000).toISOString(),
          userId: 'm1',
          fullName: 'Jenny Lau',
          email: 'manager@sarawakbizops.local',
          roles: ['Manager']
        }
      }),
      'GET /customers': () => (refreshes === 1 ? { status: 401 } : { body: customers })
    })
    renderApp('/customers')

    expect(await screen.findByText(customers[0].companyName)).toBeInTheDocument()
    const bearer = (call: unknown[]) => new Headers((call[1] as RequestInit | undefined)?.headers).get('Authorization')
    // The page passes no token: the first call carries the one from load, the retry the renewed one.
    expect(callsTo(fetchMock, 'GET', '/customers').map(bearer)).toEqual(['Bearer token-1', 'Bearer token-2'])
    expect(callsTo(fetchMock, 'POST', '/auth/refresh').map(bearer)).toEqual([null, null])
  })

  it('signing out ends the session on the server too', async () => {
    signInAs('Manager', 'Jenny Lau')
    const fetchMock = stubApi({
      'GET /customers': { body: [] },
      'GET /equipment': { body: [] },
      'POST /auth/logout': { status: 204 }
    })
    renderApp('/')

    await userEvent.click(await screen.findByRole('button', { name: 'Log out' }))

    expect(await screen.findByRole('button', { name: 'Sign in' })).toBeInTheDocument()
    expect(callsTo(fetchMock, 'POST', '/auth/logout')).toHaveLength(1)
  })
})
