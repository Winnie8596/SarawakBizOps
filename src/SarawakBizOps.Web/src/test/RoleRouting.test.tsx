import { screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it } from 'vitest'
import { cursorPage, paged } from './fixtures'
import { renderApp, signInAs, stubApi } from './utils'

// The API is what really enforces access (see EndpointAccessTests); these prove the UI matches:
// guarded routes bounce the wrong role, and the sidebar only offers what a role may open.

const quiet = () =>
  stubApi({
    'GET /customers': { body: [] },
    'GET /equipment': { body: [] },
    'GET /users': { body: [] },
    'GET /service-requests': { body: [] },
    'GET /work-orders': { body: [] },
    'GET /work-orders/mine': { body: [] },
    'GET /parts': { body: paged([]) },
    'GET /inventory-transactions': { body: cursorPage([]) },
    'GET /reports': { body: [] }
  })

const sidebarLinks = () =>
  within(screen.getByRole('navigation'))
    .getAllByRole('link')
    .map(a => a.textContent)

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

  it.each(['Manager', 'ServiceStaff', 'WarehouseStaff'])(
    'bounces %s from the Users page back to the dashboard',
    async role => {
      signInAs(role, 'Sam Tester')
      quiet()
      renderApp('/users')

      expect(await screen.findByRole('heading', { name: 'Welcome back, Sam' })).toBeInTheDocument()
      expect(screen.queryByRole('heading', { name: 'Users' })).not.toBeInTheDocument()
    }
  )

  it('bounces WarehouseStaff from the service requests page', async () => {
    signInAs('WarehouseStaff', 'Sam Tester')
    quiet()
    renderApp('/service-requests')

    expect(await screen.findByRole('heading', { name: 'Welcome back, Sam' })).toBeInTheDocument()
    expect(screen.queryByRole('heading', { name: 'Service requests' })).not.toBeInTheDocument()
  })

  it('bounces ServiceStaff from the inventory pages — they never touch stock', async () => {
    signInAs('ServiceStaff', 'Sam Tester')
    quiet()
    renderApp('/inventory')

    expect(await screen.findByRole('heading', { name: 'Welcome back, Sam' })).toBeInTheDocument()
    expect(screen.queryByRole('heading', { name: 'Inventory' })).not.toBeInTheDocument()
  })

  it.each(['/users', '/service-requests', '/work-orders', '/customers', '/inventory'])(
    'a Technician visiting %s lands on My Jobs instead',
    async path => {
      signInAs('Technician')
      quiet()
      renderApp(path)

      expect(await screen.findByRole('heading', { name: 'My Jobs' })).toBeInTheDocument()
    }
  )
})

describe('Sidebar', () => {
  it.each([
    [
      'Admin',
      ['Dashboard', 'Customers', 'Equipment', 'Service Requests', 'Work Orders', 'Reports', 'Inventory', 'Users']
    ],
    [
      'Manager',
      ['Dashboard', 'Customers', 'Equipment', 'Service Requests', 'Work Orders', 'Reports', 'AI Insights', 'Inventory']
    ],
    ['ServiceStaff', ['Dashboard', 'Customers', 'Equipment', 'Service Requests', 'Work Orders', 'Reports']],
    ['WarehouseStaff', ['Dashboard', 'Inventory']]
  ])('offers %s only the pages that role can open', async (role, expected) => {
    signInAs(role)
    quiet()
    renderApp('/')

    await screen.findByRole('heading', { name: /Welcome back/ })
    expect(sidebarLinks()).toEqual(expected)
  })

  it('gives a Technician no office sidebar at all — the mobile job list has none', async () => {
    signInAs('Technician')
    quiet()
    renderApp('/')

    await screen.findByRole('heading', { name: 'My Jobs' })
    expect(screen.queryByRole('navigation')).not.toBeInTheDocument()
  })
})

describe('Menu button (phones)', () => {
  it('opens and closes the links, and closes once a page is opened', async () => {
    signInAs('Manager')
    quiet()
    renderApp('/customers')

    const toggle = await screen.findByRole('button', { name: 'Open menu' })
    const nav = screen.getByRole('navigation', { name: 'Main' })
    expect(toggle).toHaveAttribute('aria-expanded', 'false')
    expect(toggle).toHaveAttribute('aria-controls', nav.id)
    expect(nav).not.toHaveClass('is-open')

    await userEvent.click(toggle)
    expect(screen.getByRole('button', { name: 'Close menu' })).toHaveAttribute('aria-expanded', 'true')
    expect(nav).toHaveClass('is-open')

    await userEvent.click(within(nav).getByRole('link', { name: 'Equipment' }))
    expect(await screen.findByRole('button', { name: 'Open menu' })).toHaveAttribute('aria-expanded', 'false')
    expect(nav).not.toHaveClass('is-open')
  })
})
