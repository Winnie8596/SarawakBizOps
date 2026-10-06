import { screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it } from 'vitest'
import type { TechnicianAssistance } from '../types'
import { workOrder } from './fixtures'
import { callsTo, renderApp, signInAs, stubApi } from './utils'

const DISCLAIMER = 'AI-generated assistance — not a certified diagnosis. Check everything on site before acting; nothing here is applied to the job.'

function assistance(overrides: Partial<TechnicianAssistance> = {}): TechnicianAssistance {
  return {
    workOrderId: 41,
    disclaimer: DISCLAIMER,
    generatedBy: 'Demo mock assistant',
    generatedAt: '2026-09-30T02:00:00',
    pastCasesConsidered: 2,
    possibleCauses: [
      { cause: 'Motor protection tripping', likelihood: 'High', reasoning: 'The job mentions “trip”.' },
      { cause: 'Electrical supply or regulation fault', likelihood: 'Medium', reasoning: 'The job mentions “start”.' }
    ],
    suggestedChecks: ['Measure the running current on each phase.', 'Check the overload relay setting.'],
    relevantPastCases: [{
      workOrderId: 12,
      completedAt: '2026-05-25T06:00:00',
      problemDescription: 'Air compressor trips on overload during the hot afternoon shift.',
      diagnosis: 'Clogged intake filter.',
      whyRelevant: 'Also involved “trip” on this equipment.'
    }],
    relevantParts: [{
      partId: 301, partNumber: 'REL-OVL-25A', name: 'Thermal overload relay 18–25 A', unit: 'pcs',
      quantityOnHand: 3, whyRelevant: 'A likely repair uses a relay.'
    }],
    ...overrides
  }
}

const inProgress = workOrder({ status: 'InProgress', startedAt: '2026-09-21T01:00:00' })

describe('AI assistant on the technician Diagnosis step', () => {
  it('shows labelled suggestions and copies them into the notes, never into the diagnosis or the server', async () => {
    signInAs('Technician')
    const fetchMock = stubApi({
      'GET /work-orders/41': { body: inProgress },
      'POST /ai/technician-assistant': { body: assistance() }
    })
    renderApp('/tech/jobs/41')

    await userEvent.type(await screen.findByLabelText(/What have you noticed on site/), 'Hot to the touch')
    await userEvent.click(screen.getByRole('button', { name: 'Get suggestions' }))

    expect(await screen.findByRole('note')).toHaveTextContent('AI-generated assistance — not a certified diagnosis')
    expect(screen.getByText('Motor protection tripping')).toBeInTheDocument()
    expect(screen.getByText('Motor protection tripping').closest('li')).toHaveTextContent(/^High/)
    expect(screen.getByText('Measure the running current on each phase.')).toBeInTheDocument()
    expect(screen.getByText('Air compressor trips on overload during the hot afternoon shift.')).toBeInTheDocument()
    expect(screen.queryByRole('link', { name: '#12' })).not.toBeInTheDocument() // a technician may not open it
    expect(screen.getByText(/3 pcs in stock/)).toBeInTheDocument()
    const [, init] = callsTo(fetchMock, 'POST', '/ai/technician-assistant')[0]
    expect(JSON.parse(String(init?.body))).toEqual({ workOrderId: 41, observations: 'Hot to the touch' })

    await userEvent.type(screen.getByLabelText('Notes (optional)'), 'Customer on site.')
    await userEvent.click(screen.getByRole('button', { name: 'Copy to notes' }))

    const notes = screen.getByLabelText('Notes (optional)') as HTMLTextAreaElement
    expect(notes.value).toMatch(/^Customer on site\.\n\nAI suggestions \(not a certified diagnosis\):\n/)
    expect(notes.value).toContain('Motor protection tripping (High)')
    expect(notes.value).toContain('Parts: REL-OVL-25A')
    expect(screen.getByLabelText('What is wrong?')).toHaveValue('')
    expect(screen.getByRole('status')).toHaveTextContent('saved only with the job')
    // Nothing was written: apart from restoring the session, the only calls are loading the job and asking the assistant.
    expect(fetchMock.mock.calls
      .filter(([input]) => !String(input).includes('/auth/'))
      .map(([, i]) => (i?.method ?? 'GET').toUpperCase())).toEqual(['GET', 'POST'])
  })

  it('says so when the assistant is unavailable, and the job carries on', async () => {
    signInAs('Technician')
    stubApi({
      'GET /work-orders/41': { body: inProgress },
      'POST /ai/technician-assistant': {
        status: 503,
        body: { title: 'Service unavailable', detail: 'The AI assistant is unavailable right now. Carry on with the job; you can try again later.' }
      }
    })
    renderApp('/tech/jobs/41')

    await userEvent.click(await screen.findByRole('button', { name: 'Get suggestions' }))

    expect(await screen.findByRole('alert')).toHaveTextContent('The AI assistant is unavailable right now.')
    expect(screen.queryByRole('note')).not.toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Get suggestions' })).toBeEnabled()
    expect(screen.getByLabelText('What is wrong?')).toBeEnabled()
  })

  it('shows the rate-limit message', async () => {
    signInAs('Technician')
    stubApi({
      'GET /work-orders/41': { body: inProgress },
      'POST /ai/technician-assistant': {
        status: 429,
        body: { title: 'Too many requests', detail: 'You have asked the AI assistant too often. Wait a minute and try again.' }
      }
    })
    renderApp('/tech/jobs/41')

    await userEvent.click(await screen.findByRole('button', { name: 'Get suggestions' }))

    expect(await screen.findByRole('alert')).toHaveTextContent('asked the AI assistant too often')
  })

  it('explains an empty history', async () => {
    signInAs('Technician')
    stubApi({
      'GET /work-orders/41': { body: inProgress },
      'POST /ai/technician-assistant': { body: assistance({ pastCasesConsidered: 0, relevantPastCases: [], relevantParts: [] }) }
    })
    renderApp('/tech/jobs/41')

    await userEvent.click(await screen.findByRole('button', { name: 'Get suggestions' }))

    expect(await screen.findByText('No earlier completed jobs on this equipment.')).toBeInTheDocument()
    expect(screen.getByText('No particular parts suggested.')).toBeInTheDocument()
  })

  it('is not offered before the job is started', async () => {
    signInAs('Technician')
    stubApi({ 'GET /work-orders/41': { body: workOrder({ status: 'Assigned' }) } })
    renderApp('/tech/jobs/41')

    expect(await screen.findByRole('button', { name: 'Start job' })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Get suggestions' })).not.toBeInTheDocument()
  })
})

describe('AI assistant on the office work order page', () => {
  it('is offered to a Manager on an open job, with earlier jobs linked', async () => {
    signInAs('Manager')
    stubApi({
      'GET /work-orders/41': { body: inProgress },
      'POST /ai/technician-assistant': { body: assistance() }
    })
    renderApp('/work-orders/41')

    const panel = (await screen.findByRole('heading', { name: 'AI assistant' })).closest('section')!
    await userEvent.click(within(panel).getByRole('button', { name: 'Get suggestions' }))

    expect(await within(panel).findByRole('link', { name: '#12' })).toHaveAttribute('href', '/work-orders/12')
    expect(within(panel).getByRole('note')).toHaveTextContent(DISCLAIMER)
    expect(within(panel).queryByRole('button', { name: 'Copy to notes' })).not.toBeInTheDocument()
  })

  it('is not offered to ServiceStaff, nor on a finished job', async () => {
    signInAs('ServiceStaff')
    stubApi({ 'GET /work-orders/41': { body: inProgress } })
    const { unmount } = renderApp('/work-orders/41')
    expect(await screen.findByText('Work order #41', { exact: false })).toBeInTheDocument()
    expect(screen.queryByRole('heading', { name: 'AI assistant' })).not.toBeInTheDocument()
    unmount()

    signInAs('Manager')
    stubApi({ 'GET /work-orders/41': { body: workOrder({ status: 'Completed', completedAt: '2026-09-21T03:00:00' }) } })
    renderApp('/work-orders/41')
    expect(await screen.findByText('Work order #41', { exact: false })).toBeInTheDocument()
    expect(screen.queryByRole('heading', { name: 'AI assistant' })).not.toBeInTheDocument()
  })
})
