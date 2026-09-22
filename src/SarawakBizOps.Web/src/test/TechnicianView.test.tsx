import { screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it } from 'vitest'
import { workOrder } from './fixtures'
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
      'GET /work-orders/41': { body: workOrder({ status: 'InProgress', startedAt: '2026-09-21T01:00:00' }) },
      'PATCH /work-orders/41/diagnosis': ({ body }) => ({
        body: workOrder({ status: 'InProgress', ...(body as object) })
      }),
      'POST /work-orders/41/complete': {
        body: workOrder({ status: 'Completed', diagnosis: 'Loose belt', workPerformed: 'Retensioned it', completedAt: '2026-09-21T02:00:00' })
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
    expect(JSON.parse(String(patchInit?.body))).toEqual({ diagnosis: 'Loose belt', workPerformed: 'Retensioned it' })
    expect(callsTo(fetchMock, 'POST', '/work-orders/41/complete')).toHaveLength(1)
  })

  it('shows a read-only view once approved', async () => {
    signInAs('Technician')
    stubApi({
      'GET /work-orders/41': {
        body: workOrder({
          status: 'Approved', diagnosis: 'Loose belt', workPerformed: 'Retensioned it', approvedAt: '2026-09-22T01:00:00'
        })
      }
    })
    renderApp('/tech/jobs/41')

    expect(await screen.findByText('Approved — read only')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Start job' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Complete job' })).not.toBeInTheDocument()
  })

  it('shows a not-found message for another technician’s job', async () => {
    signInAs('Technician')
    stubApi({ 'GET /work-orders/999': { status: 404, body: { title: 'Not found' } } })
    renderApp('/tech/jobs/999')

    expect(await screen.findByText('That record could not be found.')).toBeInTheDocument()
  })
})
