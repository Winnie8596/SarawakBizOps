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
          token: 'jwt', expiresAtUtc: new Date(Date.now() + 3_600_000).toISOString(),
          userId: 'm1', fullName: 'Jenny Lau', email: 'manager@sarawakbizops.local', roles: ['Manager']
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

  it('signing out ends the session on the server too', async () => {
    signInAs('Manager', 'Jenny Lau')
    const fetchMock = stubApi({
      'GET /customers': { body: [] }, 'GET /equipment': { body: [] }, 'POST /auth/logout': { status: 204 }
    })
    renderApp('/')

    await userEvent.click(await screen.findByRole('button', { name: 'Log out' }))

    expect(await screen.findByRole('button', { name: 'Sign in' })).toBeInTheDocument()
    expect(callsTo(fetchMock, 'POST', '/auth/logout')).toHaveLength(1)
  })
})
