import { screen, within } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { renderApp, signInAs, stubApi } from './utils'

// The API is what really enforces access (see EndpointAccessTests); these prove the UI matches:
// guarded routes bounce the wrong role, and the sidebar only offers what a role may open.

const quiet = () => stubApi({
  'GET /customers': { body: [] },
  'GET /equipment': { body: [] },
  'GET /users': { body: [] },
  'GET /service-requests': { body: [] }
})

const sidebarLinks = () => within(screen.getByRole('navigation')).getAllByRole('link').map(a => a.textContent)

describe('Protected routes', () => {
  it('sends a visitor without a session to the login page', async () => {
    quiet()
    renderApp('/customers')

    expect(await screen.findByRole('button', { name: 'Sign in' })).toBeInTheDocument()
  })

  it('lets an Admin open the Users page', async () => {
    signInAs('Admin')
    quiet()
    renderApp('/users')

    expect(await screen.findByRole('heading', { name: 'Users' })).toBeInTheDocument()
  })

  it.each(['Manager', 'ServiceStaff', 'Technician', 'WarehouseStaff'])(
    'bounces %s from the Users page back to the dashboard',
    async role => {
      signInAs(role, 'Sam Tester')
      quiet()
      renderApp('/users')

      expect(await screen.findByRole('heading', { name: 'Welcome back, Sam' })).toBeInTheDocument()
      expect(screen.queryByRole('heading', { name: 'Users' })).not.toBeInTheDocument()
    })

  it.each(['Technician', 'WarehouseStaff'])('bounces %s from the service requests page', async role => {
    signInAs(role, 'Sam Tester')
    quiet()
    renderApp('/service-requests')

    expect(await screen.findByRole('heading', { name: 'Welcome back, Sam' })).toBeInTheDocument()
    expect(screen.queryByRole('heading', { name: 'Service requests' })).not.toBeInTheDocument()
  })
})

describe('Sidebar', () => {
  it.each([
    ['Admin', ['Dashboard', 'Customers', 'Equipment', 'Service Requests', 'Users']],
    ['Manager', ['Dashboard', 'Customers', 'Equipment', 'Service Requests']],
    ['ServiceStaff', ['Dashboard', 'Customers', 'Equipment', 'Service Requests']],
    ['Technician', ['Dashboard']],
    ['WarehouseStaff', ['Dashboard']]
  ])('offers %s only the pages that role can open', async (role, expected) => {
    signInAs(role)
    quiet()
    renderApp('/')

    await screen.findByRole('heading', { name: /Welcome back/ })
    expect(sidebarLinks()).toEqual(expected)
  })
})
