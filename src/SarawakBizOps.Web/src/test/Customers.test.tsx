import { screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it } from 'vitest'
import { customers } from './fixtures'
import { callsTo, renderApp, signInAs, stubApi } from './utils'

describe('Creating a customer', () => {
  it('omits blank optional fields instead of sending them as empty strings', async () => {
    // Regression test: [EmailAddress] on the API rejects "" (only a missing field passes), so an
    // empty optional input must be left out of the payload, not sent as ''.
    signInAs('ServiceStaff')
    const fetchMock = stubApi({
      'GET /customers': { body: customers },
      'POST /customers': { body: { ...customers[0], id: 99 } }
    })
    renderApp('/customers')

    await userEvent.click(await screen.findByRole('button', { name: 'New customer' }))
    await userEvent.type(screen.getByLabelText('Company name'), 'New Co Sdn Bhd')
    await userEvent.click(screen.getByRole('button', { name: 'Save customer' }))

    const [, init] = callsTo(fetchMock, 'POST', '/customers')[0]
    expect(JSON.parse(String(init?.body))).toEqual({ companyName: 'New Co Sdn Bhd' })
  })

  it('trims whitespace-only optional fields the same way', async () => {
    signInAs('ServiceStaff')
    const fetchMock = stubApi({
      'GET /customers': { body: customers },
      'POST /customers': { body: { ...customers[0], id: 99 } }
    })
    renderApp('/customers')

    await userEvent.click(await screen.findByRole('button', { name: 'New customer' }))
    await userEvent.type(screen.getByLabelText('Company name'), 'New Co Sdn Bhd')
    await userEvent.type(screen.getByLabelText('Email'), '   ')
    await userEvent.click(screen.getByRole('button', { name: 'Save customer' }))

    const [, init] = callsTo(fetchMock, 'POST', '/customers')[0]
    expect(JSON.parse(String(init?.body))).toEqual({ companyName: 'New Co Sdn Bhd' })
  })
})
