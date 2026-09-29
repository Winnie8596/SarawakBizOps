import { screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it } from 'vitest'
import { serviceRequest, technicians, workOrder, workOrderPart } from './fixtures'
import { callsTo, renderApp, signInAs, stubApi } from './utils'

describe('Work orders list (office)', () => {
  it('lists work orders and links to their detail page', async () => {
    signInAs('Manager')
    stubApi({
      'GET /work-orders': { body: [workOrder(), workOrder({ id: 42, status: 'Completed' })] }
    })
    renderApp('/work-orders')

    expect(await screen.findByRole('link', { name: '#41' })).toBeInTheDocument()
    expect(screen.getByRole('link', { name: '#42' })).toBeInTheDocument()
  })

  it('shows an empty state with no work orders yet', async () => {
    signInAs('ServiceStaff')
    stubApi({ 'GET /work-orders': { body: [] } })
    renderApp('/work-orders')

    expect(await screen.findByText('No work orders yet')).toBeInTheDocument()
  })
})

describe('Work order detail (office)', () => {
  it('lets a Manager approve a completed job and then hides the button', async () => {
    signInAs('Manager')
    const fetchMock = stubApi({
      'GET /work-orders/41': { body: workOrder({ status: 'Completed', diagnosis: 'Loose belt', workPerformed: 'Retensioned it', completedAt: '2026-09-21T01:00:00' }) },
      'POST /work-orders/41/approve': { body: workOrder({ status: 'Approved', diagnosis: 'Loose belt', workPerformed: 'Retensioned it', approvedAt: '2026-09-22T01:00:00' }) }
    })
    renderApp('/work-orders/41')

    expect(await screen.findByText('Loose belt')).toBeInTheDocument()
    await userEvent.click(screen.getByRole('button', { name: 'Approve' }))

    const heading = await screen.findByRole('heading', { name: /Work order #41/ })
    expect(within(heading).getByText('Approved')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Approve' })).not.toBeInTheDocument()
    expect(callsTo(fetchMock, 'POST', '/work-orders/41/approve')).toHaveLength(1)
  })

  it('offers ServiceStaff no approve button on a completed job', async () => {
    signInAs('ServiceStaff')
    stubApi({ 'GET /work-orders/41': { body: workOrder({ status: 'Completed' }) } })
    renderApp('/work-orders/41')

    await screen.findByText('Work order #41')
    expect(screen.queryByRole('button', { name: 'Approve' })).not.toBeInTheDocument()
    expect(screen.getByText('Waiting for a Manager to approve this job.')).toBeInTheDocument()
  })
})

describe('Assigning a technician from a service request', () => {
  it('lets a Manager assign an approved request and links to the new work order', async () => {
    signInAs('Manager')
    const approved = serviceRequest({ status: 'Approved', approvedByName: 'Jenny Lau', approvedAt: '2026-09-21T03:00:00' })
    // Mirrors the real API: once /assign succeeds, the request's own status (and workOrderId) has
    // moved on, so the page's refetch after assigning must see 'Assigned', not the stale 'Approved'.
    let assigned = false
    const fetchMock = stubApi({
      'GET /service-requests/7': () => ({
        body: assigned ? { ...approved, status: 'Assigned', workOrderId: 55 } : approved
      }),
      'GET /technicians': { body: technicians },
      'POST /service-requests/7/assign': () => {
        assigned = true
        return { body: workOrder({ id: 55, status: 'Assigned' }) }
      }
    })
    renderApp('/service-requests/7')

    await userEvent.click(await screen.findByRole('button', { name: 'Assign to a technician' }))
    await userEvent.selectOptions(
      screen.getByRole('combobox', { name: 'Technician' }),
      screen.getByRole('option', { name: 'Ahmad Bujang (0 open jobs)' })
    )
    await userEvent.click(screen.getByRole('button', { name: 'Assign' }))

    expect(await screen.findByRole('link', { name: 'View the work order' })).toHaveAttribute('href', '/work-orders/55')
    const [, init] = callsTo(fetchMock, 'POST', '/service-requests/7/assign')[0]
    expect(JSON.parse(String(init?.body))).toEqual({ technicianId: 'tech-1', rowVersion: 'AAAAAAAAB9E=' })
  })

  it('shows the API message when assignment is refused', async () => {
    signInAs('Manager')
    stubApi({
      'GET /service-requests/7': { body: serviceRequest({ status: 'Approved' }) },
      'GET /technicians': { body: technicians },
      'POST /service-requests/7/assign': {
        status: 400,
        body: { title: 'Bad request', detail: 'The selected user is not a Technician.' }
      }
    })
    renderApp('/service-requests/7')

    await userEvent.click(await screen.findByRole('button', { name: 'Assign to a technician' }))
    await userEvent.selectOptions(screen.getByRole('combobox', { name: 'Technician' }), 'tech-1')
    await userEvent.click(screen.getByRole('button', { name: 'Assign' }))

    expect(await screen.findByText('The selected user is not a Technician.')).toBeInTheDocument()
  })

  it('does not offer assignment to ServiceStaff', async () => {
    signInAs('ServiceStaff')
    stubApi({ 'GET /service-requests/7': { body: serviceRequest({ status: 'Approved' }) } })
    renderApp('/service-requests/7')

    await screen.findByText('Waiting for a Manager to assign this job to a technician.')
    expect(screen.queryByRole('button', { name: 'Assign to a technician' })).not.toBeInTheDocument()
  })

  it('links an already-assigned request straight to its work order', async () => {
    signInAs('Manager')
    stubApi({ 'GET /service-requests/7': { body: serviceRequest({ status: 'Assigned', workOrderId: 55 }) } })
    renderApp('/service-requests/7')

    expect(await screen.findByRole('link', { name: 'View the work order' })).toHaveAttribute('href', '/work-orders/55')
  })

  it('falls back to the Work Orders page if an assigned request has no workOrderId', async () => {
    signInAs('Manager')
    stubApi({ 'GET /service-requests/7': { body: serviceRequest({ status: 'Assigned', workOrderId: null }) } })
    renderApp('/service-requests/7')

    const hint = await screen.findByText(/This request has been assigned to a technician/)
    expect(within(hint).getByRole('link', { name: 'Work Orders' })).toHaveAttribute('href', '/work-orders')
  })
})

describe('Cancelling a work order', () => {
  it('lets a Manager cancel an assigned job with a reason', async () => {
    signInAs('Manager')
    const fetchMock = stubApi({
      'GET /work-orders/41': { body: workOrder({ status: 'Assigned' }) },
      'POST /work-orders/41/cancel': ({ body }) => ({
        body: workOrder({
          status: 'Cancelled', cancelledByName: 'Test User', cancelledAt: '2026-09-22T03:00:00',
          cancellationReason: (body as { reason: string }).reason
        })
      })
    })
    renderApp('/work-orders/41')

    await userEvent.click(await screen.findByRole('button', { name: 'Cancel job…' }))
    const confirm = screen.getByRole('button', { name: 'Confirm cancellation' })
    expect(confirm).toBeDisabled()

    await userEvent.type(screen.getByLabelText('Reason for cancelling'), 'Customer withdrew the job')
    await userEvent.click(confirm)

    const heading = await screen.findByRole('heading', { name: /Work order #41/ })
    expect(within(heading).getByText('Cancelled')).toBeInTheDocument()
    const [, init] = callsTo(fetchMock, 'POST', '/work-orders/41/cancel')[0]
    expect(JSON.parse(String(init?.body))).toEqual({ reason: 'Customer withdrew the job', rowVersion: 'AAAAAAAAB9E=' })
  })

  it('does not offer a cancel button once the job is Completed', async () => {
    signInAs('Manager')
    stubApi({
      'GET /work-orders/41': { body: workOrder({ status: 'Completed', diagnosis: 'Loose belt', workPerformed: 'Retensioned it' }) }
    })
    renderApp('/work-orders/41')

    await screen.findByText('Loose belt')
    expect(screen.queryByRole('button', { name: 'Cancel job…' })).not.toBeInTheDocument()
  })

  it('offers no cancel button to a non-Manager', async () => {
    signInAs('ServiceStaff')
    stubApi({ 'GET /work-orders/41': { body: workOrder({ status: 'Assigned' }) } })
    renderApp('/work-orders/41')

    await screen.findByText('Work order #41')
    expect(screen.queryByRole('button', { name: 'Cancel job…' })).not.toBeInTheDocument()
  })
})

describe('Parts on a work order (office)', () => {
  it('lists the parts used with RM line totals and the job total', async () => {
    signInAs('Manager')
    stubApi({
      'GET /work-orders/41': {
        body: workOrder({
          status: 'Completed',
          parts: [
            workOrderPart(),
            workOrderPart({ id: 502, partNumber: 'OIL-HYD-46', partName: 'Hydraulic oil', unit: 'L', quantity: 12, unitPrice: 14.9, lineTotal: 178.8 })
          ],
          partsTotal: 3079.8
        })
      }
    })
    renderApp('/work-orders/41')

    const table = await screen.findByRole('table')
    expect(within(table).getByText('12 L')).toBeInTheDocument()
    expect(within(table).getByText('RM 178.80')).toBeInTheDocument()
    expect(within(table).getByText('RM 3,079.80')).toBeInTheDocument()
  })

  it('shows what a paused job is waiting for and lets a Manager cancel it', async () => {
    signInAs('Manager')
    stubApi({
      'GET /work-orders/41': {
        body: workOrder({
          status: 'PendingParts',
          startedAt: '2026-09-21T01:00:00',
          pendingPartsReason: 'Need 6 m of belting',
          pendingPartNumber: 'BLT-CNV-600',
          pendingPartName: 'Conveyor belt, 600 mm',
          pendingPartUnit: 'm',
          pendingPartQuantity: 6,
          pendingPartsAt: '2026-09-21T02:00:00'
        })
      }
    })
    renderApp('/work-orders/41')

    expect(await screen.findByRole('heading', { name: 'Waiting for parts' })).toBeInTheDocument()
    expect(screen.getByText('BLT-CNV-600')).toBeInTheDocument()
    expect(screen.getByText('No parts used on this job.')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Cancel job…' })).toBeInTheDocument()
  })
})
