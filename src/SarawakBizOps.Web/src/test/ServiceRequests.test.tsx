import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it } from 'vitest'
import { customers, rajangEquipment, serviceRequest } from './fixtures'
import { callsTo, renderApp, signInAs, stubApi } from './utils'

describe('Service requests list', () => {
  it('gives ServiceStaff the New service request button and shows every status', async () => {
    signInAs('ServiceStaff')
    const fetchMock = stubApi({
      'GET /customers': { body: customers },
      'GET /service-requests': { body: [serviceRequest(), serviceRequest({ id: 8, status: 'Approved' })] }
    })
    renderApp('/service-requests')

    expect(await screen.findByRole('button', { name: 'New service request' })).toBeInTheDocument()
    expect(await screen.findByRole('link', { name: '#7' })).toBeInTheDocument()
    expect(screen.getByRole('link', { name: '#8' })).toBeInTheDocument()
    expect(callsTo(fetchMock, 'GET', '/service-requests')[0][0]).not.toContain('status=')
  })

  it('opens a Manager on the New requests and has no create button', async () => {
    signInAs('Manager')
    const fetchMock = stubApi({
      'GET /customers': { body: customers },
      'GET /service-requests': { body: [serviceRequest()] }
    })
    renderApp('/service-requests')

    expect(await screen.findByRole('link', { name: '#7' })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'New service request' })).not.toBeInTheDocument()
    expect(String(callsTo(fetchMock, 'GET', '/service-requests')[0][0])).toContain('status=New')
    expect(screen.getByRole('combobox', { name: 'Status' })).toHaveValue('New')
  })

  it('shows a filter-aware empty state and an error state', async () => {
    signInAs('Manager')
    stubApi({
      'GET /customers': { body: customers },
      'GET /service-requests': { body: [] }
    })
    renderApp('/service-requests')

    expect(await screen.findByText('No requests match these filters')).toBeInTheDocument()

    await userEvent.selectOptions(screen.getByRole('combobox', { name: 'Status' }), '')
    expect(await screen.findByText('No service requests yet')).toBeInTheDocument()
  })

  it('shows the API message when the list cannot be loaded', async () => {
    signInAs('ServiceStaff')
    stubApi({
      'GET /customers': { body: customers },
      'GET /service-requests': { status: 500, body: { title: 'Server error', detail: 'The database is unavailable.' } }
    })
    renderApp('/service-requests')

    expect(await screen.findByText('The database is unavailable.')).toBeInTheDocument()
  })
})

describe('Raising a request', () => {
  it('offers only the chosen customer’s serviceable equipment and posts the request', async () => {
    signInAs('ServiceStaff')
    const created = serviceRequest({ id: 31 })
    const fetchMock = stubApi({
      'GET /customers': { body: customers },
      'GET /equipment': ({ url }) => ({
        body: url.searchParams.get('customerId') === '2' ? rajangEquipment : []
      }),
      'GET /service-requests': { body: [] },
      'POST /service-requests': { status: 201, body: created }
    })
    renderApp('/service-requests')
    await userEvent.click(await screen.findByRole('button', { name: 'New service request' }))

    const form = screen.getByRole('heading', { name: 'New service request' }).closest('form')!
    const equipmentSelect = () => within(form).getByRole('combobox', { name: 'Equipment' })
    expect(equipmentSelect()).toBeDisabled()

    await userEvent.selectOptions(within(form).getByRole('combobox', { name: 'Customer' }), '2')
    await waitFor(() => expect(equipmentSelect()).toBeEnabled())

    // BR-10 in the UI: this customer's equipment, minus the retired unit the API would refuse.
    const offered = Array.from((equipmentSelect() as HTMLSelectElement).options).map(o => o.textContent)
    expect(offered).toEqual([
      'Select equipment…',
      'RTM-BSW-001 — Band Saw',
      'RTM-CNV-003 — Log Conveyor'
    ])

    const submit = within(form).getByRole('button', { name: 'Submit request' })
    expect(submit).toBeDisabled()

    await userEvent.selectOptions(equipmentSelect(), '20')
    await userEvent.selectOptions(within(form).getByRole('combobox', { name: 'Priority' }), 'High')
    await userEvent.type(within(form).getByLabelText('What is wrong?'), 'Blade drifts after warm-up')
    await userEvent.click(submit)

    expect(await screen.findByText(/Request #31 submitted/)).toBeInTheDocument()
    const [, init] = callsTo(fetchMock, 'POST', '/service-requests')[0]
    expect(JSON.parse(String(init?.body))).toEqual({
      customerId: 2, equipmentId: 20, problemDescription: 'Blade drifts after warm-up', priority: 'High'
    })
  })

  it('shows the API message when the request is refused', async () => {
    signInAs('ServiceStaff')
    stubApi({
      'GET /customers': { body: customers },
      'GET /equipment': { body: rajangEquipment },
      'GET /service-requests': { body: [] },
      'POST /service-requests': {
        status: 400,
        body: { title: 'Bad request', detail: 'The selected equipment does not belong to this customer.' }
      }
    })
    renderApp('/service-requests')
    await userEvent.click(await screen.findByRole('button', { name: 'New service request' }))
    const form = screen.getByRole('heading', { name: 'New service request' }).closest('form')!

    await userEvent.selectOptions(within(form).getByRole('combobox', { name: 'Customer' }), '2')
    await waitFor(() => expect(within(form).getByRole('combobox', { name: 'Equipment' })).toBeEnabled())
    await userEvent.selectOptions(within(form).getByRole('combobox', { name: 'Equipment' }), '20')
    await userEvent.type(within(form).getByLabelText('What is wrong?'), 'Anything')
    await userEvent.click(within(form).getByRole('button', { name: 'Submit request' }))

    expect(await screen.findByText('The selected equipment does not belong to this customer.')).toBeInTheDocument()
  })
})

describe('Reviewing a request', () => {
  it('lets a Manager approve a New request and hides the buttons afterwards', async () => {
    signInAs('Manager')
    const fetchMock = stubApi({
      'GET /service-requests/7': { body: serviceRequest() },
      'POST /service-requests/7/approve': {
        body: serviceRequest({ status: 'Approved', approvedByName: 'Jenny Lau', approvedAt: '2026-09-21T03:00:00' })
      }
    })
    renderApp('/service-requests/7')

    await userEvent.click(await screen.findByRole('button', { name: 'Approve' }))

    expect(await screen.findByText(/Jenny Lau/)).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Approve' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Reject' })).not.toBeInTheDocument()
    expect(callsTo(fetchMock, 'POST', '/service-requests/7/approve')).toHaveLength(1)
  })

  it('requires a reason before a rejection can be confirmed, then shows it', async () => {
    signInAs('Manager')
    const fetchMock = stubApi({
      'GET /service-requests/7': { body: serviceRequest() },
      'POST /service-requests/7/reject': ({ body }) => ({
        body: serviceRequest({
          status: 'Rejected', rejectedByName: 'Jenny Lau', rejectedAt: '2026-09-21T03:00:00',
          rejectionReason: (body as { reason: string }).reason
        })
      })
    })
    renderApp('/service-requests/7')

    await userEvent.click(await screen.findByRole('button', { name: 'Reject' }))
    const confirm = screen.getByRole('button', { name: 'Confirm rejection' })
    expect(confirm).toBeDisabled()

    await userEvent.type(screen.getByLabelText('Reason for rejecting'), '   ')
    expect(confirm).toBeDisabled()
    await userEvent.type(screen.getByLabelText('Reason for rejecting'), 'Out of warranty')
    await userEvent.click(confirm)

    expect(await screen.findByText('Out of warranty')).toBeInTheDocument()
    const [, init] = callsTo(fetchMock, 'POST', '/service-requests/7/reject')[0]
    expect(JSON.parse(String(init?.body))).toEqual({ reason: 'Out of warranty' })
  })

  it('shows the conflict message and refreshes when someone else already decided', async () => {
    signInAs('Manager')
    let loads = 0
    stubApi({
      'GET /service-requests/7': () => ({
        body: ++loads === 1 ? serviceRequest() : serviceRequest({ status: 'Rejected', rejectedAt: '2026-09-21T02:00:00', rejectedByName: 'Other Manager', rejectionReason: 'Duplicate' })
      }),
      'POST /service-requests/7/approve': {
        status: 409,
        body: { title: 'Conflict', detail: 'This request is Rejected and cannot be approved. Only a New request can be approved or rejected.' }
      }
    })
    renderApp('/service-requests/7')

    await userEvent.click(await screen.findByRole('button', { name: 'Approve' }))

    expect(await screen.findByRole('alert')).toHaveTextContent('This request is Rejected and cannot be approved')
    await waitFor(() => expect(screen.queryByRole('button', { name: 'Approve' })).not.toBeInTheDocument())
    expect(screen.getByText('Duplicate')).toBeInTheDocument()
  })

  it('shows ServiceStaff the request without any review buttons', async () => {
    signInAs('ServiceStaff')
    stubApi({ 'GET /service-requests/7': { body: serviceRequest() } })
    renderApp('/service-requests/7')

    expect(await screen.findByText('Blade tracking drifts after warm-up.')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Approve' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Reject' })).not.toBeInTheDocument()
    expect(screen.getByText('Waiting for a Manager to review this request.')).toBeInTheDocument()
  })

  it('offers a Manager no buttons on a request that is already decided', async () => {
    signInAs('Manager')
    stubApi({
      'GET /service-requests/7': { body: serviceRequest({ status: 'Approved', approvedByName: 'Jenny Lau', approvedAt: '2026-09-21T03:00:00' }) }
    })
    renderApp('/service-requests/7')

    expect(await screen.findByText(/Jenny Lau/)).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Approve' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Reject' })).not.toBeInTheDocument()
  })

  it('shows a not-found message for an unknown request', async () => {
    signInAs('Manager')
    stubApi({ 'GET /service-requests/999': { status: 404, body: { title: 'Not found' } } })
    renderApp('/service-requests/999')

    expect(await screen.findByText('That record could not be found.')).toBeInTheDocument()
  })

  it('shows dates in Malaysia time (UTC+8), not UTC or browser time', async () => {
    signInAs('ServiceStaff')
    // 02:30 UTC is 10:30 in Kuching.
    stubApi({ 'GET /service-requests/7': { body: serviceRequest({ createdAt: '2026-09-20T02:30:00' }) } })
    renderApp('/service-requests/7')

    expect(await screen.findByText(/20 Sep\w* 2026.*10:30 by Aina Abdullah/)).toBeInTheDocument()
  })
})
