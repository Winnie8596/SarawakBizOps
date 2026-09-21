import { screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it } from 'vitest'
import { renderApp, stubApi } from './utils'

describe('Login', () => {
  it('shows the API problem message when the credentials are wrong', async () => {
    stubApi({
      'POST /auth/login': { status: 401, body: { title: 'Unauthorized', detail: 'Invalid email or password.' } }
    })
    renderApp('/login')

    await userEvent.type(screen.getByLabelText('Password'), 'wrong-password')
    await userEvent.click(screen.getByRole('button', { name: 'Sign in' }))

    expect(await screen.findByText('Invalid email or password.')).toBeInTheDocument()
    expect(localStorage.getItem('sarawakbizops.auth')).toBeNull()
  })

  it('signs in, stores the session and lands on the dashboard', async () => {
    stubApi({
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
    expect(JSON.parse(localStorage.getItem('sarawakbizops.auth')!).roles).toEqual(['Manager'])
  })
})
