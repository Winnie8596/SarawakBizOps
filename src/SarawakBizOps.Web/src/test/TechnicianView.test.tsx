import { screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it } from 'vitest'
import { paged, part, workOrder, workOrderPart } from './fixtures'
import { callsTo, renderApp, signInAs, stubApi } from './utils'

describe('My Jobs (technician)', () => {
  it('lands a Technician on My Jobs at the root path and lists only their jobs', async () => {
    signInAs('Technician')
    stubApi({
      'GET /work-orders/mine': {
        body: [workOrder(), workOrder({ id: 42, status: 'Completed', equipmentSerialNumber: 'SPO-PMP-002' })]
      }
    })
    renderApp('/')

    expect(await screen.findByRole('heading', { name: 'My Jobs' })).toBeInTheDocument()
    expect(await screen.findByRole('link', { name: /RTM-BSW-001/ })).toBeInTheDocument()
    expect(screen.getByRole('link', { name: /SPO-PMP-002/ })).toBeInTheDocument()
  })

  it('shows an empty state with no jobs assigned', async () => {
    signInAs('Technician')
    stubApi({ 'GET /work-orders/mine': { body: [] } })
    renderApp('/tech/jobs')

    expect(await screen.findByText('No jobs assigned yet')).toBeInTheDocument()
  })
})

describe('Job detail (technician)', () => {
  it('starts an assigned job', async () => {
    signInAs('Technician')
    const fetchMock = stubApi({
      'GET /work-orders/41': { body: workOrder({ status: 'Assigned' }) },
      'POST /work-orders/41/start': { body: workOrder({ status: 'InProgress', startedAt: '2026-09-21T01:00:00' }) }
    })
    renderApp('/tech/jobs/41')

    await userEvent.click(await screen.findByRole('button', { name: 'Start job' }))

    expect(await screen.findByText('Diagnosis and work performed')).toBeInTheDocument()
    expect(callsTo(fetchMock, 'POST', '/work-orders/41/start')).toHaveLength(1)
  })

  it('keeps Complete job disabled until both fields are filled, then saves and completes', async () => {
    signInAs('Technician')
    const fetchMock = stubApi({
      // Already signed (Phase 8), so only the two fields gate the button here; see Signature.test.tsx.
      'GET /work-orders/41': {
        body: workOrder({
          status: 'InProgress',
          startedAt: '2026-09-21T01:00:00',
          hasSignature: true,
          signedAt: '2026-09-21T01:30:00'
        })
      },
      'GET /work-orders/41/signature': { body: new Blob(['png'], { type: 'image/png' }) },
      'PATCH /work-orders/41/diagnosis': ({ body }) => ({
        body: workOrder({ status: 'InProgress', ...(body as object) })
      }),
      'POST /work-orders/41/complete': {
        body: workOrder({
          status: 'Completed',
          diagnosis: 'Loose belt',
          workPerformed: 'Retensioned it',
          completedAt: '2026-09-21T02:00:00'
        })
      }
    })
    renderApp('/tech/jobs/41')

    const complete = await screen.findByRole('button', { name: 'Complete job' })
    expect(complete).toBeDisabled()

    await userEvent.type(screen.getByLabelText('What is wrong?'), 'Loose belt')
    expect(complete).toBeDisabled()
    await userEvent.type(screen.getByLabelText('What did you do?'), 'Retensioned it')
    expect(complete).toBeEnabled()

    await userEvent.click(complete)

    expect(await screen.findByText('Completed — waiting for Manager approval')).toBeInTheDocument()
    const [, patchInit] = callsTo(fetchMock, 'PATCH', '/work-orders/41/diagnosis')[0]
    expect(JSON.parse(String(patchInit?.body))).toEqual({
      diagnosis: 'Loose belt',
      workPerformed: 'Retensioned it',
      rowVersion: 'AAAAAAAAB9E='
    })
    expect(callsTo(fetchMock, 'POST', '/work-orders/41/complete')).toHaveLength(1)
  })

  it('shows a read-only view once approved', async () => {
    signInAs('Technician')
    stubApi({
      'GET /work-orders/41': {
        body: workOrder({
          status: 'Approved',
          diagnosis: 'Loose belt',
          workPerformed: 'Retensioned it',
          approvedAt: '2026-09-22T01:00:00'
        })
      }
    })
    renderApp('/tech/jobs/41')

    expect(await screen.findByText('Approved — read only')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Start job' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Complete job' })).not.toBeInTheDocument()
  })

  // ---- Phase 7: the Parts step ----

  it('adds a part picked from the catalogue with the quantity stepper, sending no price', async () => {
    signInAs('Technician')
    const fetchMock = stubApi({
      'GET /work-orders/41': { body: workOrder({ status: 'InProgress', startedAt: '2026-09-21T01:00:00' }) },
      // The server does the searching: answer like it would.
      'GET /parts': ({ url }) => {
        const catalogue = [part(), part({ id: 302, partNumber: 'FLT-AIR-HC75', name: 'Air filter element' })]
        const term = (url.searchParams.get('search') ?? '').toLowerCase()
        return { body: paged(catalogue.filter(p => p.name.toLowerCase().includes(term))) }
      },
      'POST /work-orders/41/parts': {
        body: workOrder({
          status: 'InProgress',
          parts: [workOrderPart()],
          partsTotal: 2901,
          rowVersion: 'AAAAAAAAC00='
        })
      }
    })
    renderApp('/tech/jobs/41')

    await userEvent.click(await screen.findByRole('button', { name: 'Add a part' }))
    await userEvent.type(await screen.findByLabelText('Find a part'), 'relay')
    await waitFor(() => expect(screen.queryByText(/FLT-AIR-HC75/)).not.toBeInTheDocument())
    const searches = callsTo(fetchMock, 'GET', '/parts').map(([input]) => new URL(String(input)).searchParams)
    expect(searches[searches.length - 1].get('search')).toBe('relay')
    expect(searches[searches.length - 1].get('pageSize')).toBe('20')
    await userEvent.click(screen.getByRole('button', { name: /REL-OVL-25A/ }))
    await userEvent.click(screen.getByRole('button', { name: 'More' }))
    expect(screen.getByLabelText('Quantity (pcs)')).toHaveValue(2)
    expect(screen.getByText('Cost RM 2,901.00')).toBeInTheDocument()

    await userEvent.click(screen.getByRole('button', { name: 'Add to job' }))

    expect(await screen.findByText('Parts total')).toBeInTheDocument()
    expect(screen.getAllByText('RM 2,901.00').length).toBeGreaterThan(0)
    expect(screen.queryByLabelText('Find a part')).not.toBeInTheDocument()
    const [, init] = callsTo(fetchMock, 'POST', '/work-orders/41/parts')[0]
    expect(JSON.parse(String(init?.body))).toEqual({ partId: 301, quantity: 2, rowVersion: 'AAAAAAAAB9E=' })
  })

  it('shows the server’s not-enough-stock refusal and keeps the picker open', async () => {
    signInAs('Technician')
    stubApi({
      'GET /work-orders/41': { body: workOrder({ status: 'InProgress', startedAt: '2026-09-21T01:00:00' }) },
      'GET /parts': { body: paged([part()]) },
      'POST /work-orders/41/parts': {
        status: 409,
        body: {
          title: 'Conflict',
          detail: 'Not enough stock: only 1 pcs of Thermal overload relay 18–25 A on hand, so 2 cannot be used.'
        }
      }
    })
    renderApp('/tech/jobs/41')

    await userEvent.click(await screen.findByRole('button', { name: 'Add a part' }))
    await userEvent.click(await screen.findByRole('button', { name: /REL-OVL-25A/ }))
    await userEvent.click(screen.getByRole('button', { name: 'More' }))
    await userEvent.click(screen.getByRole('button', { name: 'Add to job' }))

    expect(await screen.findByText(/Not enough stock: only 1 pcs/)).toBeInTheDocument()
    expect(screen.getByLabelText('Quantity (pcs)')).toBeInTheDocument()
  })

  it('pauses the job for a part that is out of stock, then shows what it is waiting for', async () => {
    signInAs('Technician')
    const fetchMock = stubApi({
      'GET /work-orders/41': { body: workOrder({ status: 'InProgress', startedAt: '2026-09-21T01:00:00' }) },
      'GET /parts': { body: paged([part({ quantityOnHand: 0 })]) },
      'POST /work-orders/41/pending-parts': {
        body: workOrder({
          status: 'PendingParts',
          pendingPartsReason: 'Relay on order',
          pendingPartId: 301,
          pendingPartNumber: 'REL-OVL-25A',
          pendingPartName: 'Thermal overload relay 18–25 A',
          pendingPartUnit: 'pcs',
          pendingPartQuantity: 1,
          pendingPartsAt: '2026-09-21T03:00:00'
        })
      }
    })
    renderApp('/tech/jobs/41')

    await userEvent.click(await screen.findByRole('button', { name: 'Add a part' }))
    await userEvent.click(await screen.findByRole('button', { name: /REL-OVL-25A/ }))
    expect(screen.getByRole('alert')).toHaveTextContent('Not enough stock: only 0 pcs on hand.')
    expect(screen.queryByRole('button', { name: 'Add to job' })).not.toBeInTheDocument()

    await userEvent.click(screen.getByRole('button', { name: 'Wait for this part…' }))
    await userEvent.type(screen.getByLabelText('Why is the job waiting?'), 'Relay on order')
    await userEvent.click(screen.getByRole('button', { name: 'Pause job for parts' }))

    expect(await screen.findByRole('heading', { name: 'Waiting for parts' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Resume job' })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Complete job' })).not.toBeInTheDocument()
    const [, init] = callsTo(fetchMock, 'POST', '/work-orders/41/pending-parts')[0]
    expect(JSON.parse(String(init?.body))).toEqual({
      reason: 'Relay on order',
      partId: 301,
      quantity: 1,
      rowVersion: 'AAAAAAAAB9E='
    })
  })

  it('explains why a paused job cannot resume yet', async () => {
    signInAs('Technician')
    const paused = workOrder({
      status: 'PendingParts',
      pendingPartNumber: 'REL-OVL-25A',
      pendingPartQuantity: 1,
      pendingPartUnit: 'pcs'
    })
    stubApi({
      'GET /work-orders/41': { body: paused },
      'POST /work-orders/41/resume': {
        status: 409,
        body: {
          title: 'Conflict',
          detail: 'Still waiting for parts: 1 pcs of Thermal overload relay 18–25 A needed, 0 on hand.'
        }
      }
    })
    renderApp('/tech/jobs/41')

    await userEvent.click(await screen.findByRole('button', { name: 'Resume job' }))

    expect(await screen.findByText(/Still waiting for parts/)).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Resume job' })).toBeInTheDocument()
  })

  it('returns a part line with the job’s row version', async () => {
    signInAs('Technician')
    const fetchMock = stubApi({
      'GET /work-orders/41': {
        body: workOrder({ status: 'InProgress', parts: [workOrderPart()], partsTotal: 2901 })
      },
      'DELETE /work-orders/41/parts/501': { body: workOrder({ status: 'InProgress', rowVersion: 'AAAAAAAAC00=' }) }
    })
    renderApp('/tech/jobs/41')

    await userEvent.click(await screen.findByRole('button', { name: 'Return REL-OVL-25A' }))

    expect(await screen.findByText('No parts used yet.')).toBeInTheDocument()
    const [, init] = callsTo(fetchMock, 'DELETE', '/work-orders/41/parts/501')[0]
    expect(JSON.parse(String(init?.body))).toEqual({ rowVersion: 'AAAAAAAAB9E=' })
  })

  it('shows parts read-only once the job is completed', async () => {
    signInAs('Technician')
    stubApi({
      'GET /work-orders/41': {
        body: workOrder({
          status: 'Completed',
          diagnosis: 'Relay',
          workPerformed: 'Replaced',
          parts: [workOrderPart()],
          partsTotal: 2901
        })
      }
    })
    renderApp('/tech/jobs/41')

    expect(await screen.findByText('Parts total')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /Return/ })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Add a part' })).not.toBeInTheDocument()
  })

  it('shows a not-found message for another technician’s job', async () => {
    signInAs('Technician')
    stubApi({ 'GET /work-orders/999': { status: 404, body: { title: 'Not found' } } })
    renderApp('/tech/jobs/999')

    expect(await screen.findByText('That record could not be found.')).toBeInTheDocument()
  })
})
