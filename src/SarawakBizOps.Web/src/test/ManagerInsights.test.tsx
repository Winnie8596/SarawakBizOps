import { screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it } from 'vitest'
import type { ManagerInsight, ManagerInsightTopic } from '../types'
import { formatFigure } from '../pages/ManagerInsightsPage'
import { callsTo, renderApp, signInAs, stubApi } from './utils'

const DISCLAIMER = 'AI-written summary of the figures beside it. The figures come straight from the system’s analytics; the summary may only repeat them — check them before acting.'

const TOPICS: ManagerInsightTopic[] = [
  {
    id: 'jobs-completed',
    title: 'Jobs completed',
    description: 'Jobs completed per month (Completed or Approved).',
    exampleQuestion: 'How many jobs were completed last month?'
  },
  {
    id: 'low-stock',
    title: 'Low stock',
    description: 'Active parts at or below their minimum stock level.',
    exampleQuestion: 'Which parts are low on stock?'
  }
]

function answered(overrides: Partial<ManagerInsight> = {}): ManagerInsight {
  return {
    outcome: 'Answered',
    question: 'How many jobs were completed last month?',
    generatedAt: '2026-10-02T04:00:00',
    topic: TOPICS[0],
    periodLabel: 'Sep 2026',
    period: { fromMonth: '2026-09', toMonth: '2026-10', fromUtc: '2026-08-31T16:00:00', toUtc: '2026-10-31T16:00:00' },
    definition: 'A job counts once the technician has completed it.',
    note: null,
    figures: [{ label: 'Jobs completed in Sep 2026', value: 7, unit: 'jobs' }],
    summary: 'Jobs completed, Sep 2026: Jobs completed in Sep 2026 — 7 jobs.',
    summaryUnavailableReason: null,
    disclaimer: DISCLAIMER,
    generatedBy: 'Demo mock assistant',
    refusalReason: null,
    supportedTopics: [],
    ...overrides
  }
}

describe('AI Manager Insights', () => {
  it('is in the sidebar for a Manager only', async () => {
    signInAs('Manager')
    stubApi({ 'GET /dashboard/summary': { status: 500, body: { title: 'x' } } })
    renderApp('/')
    expect(await screen.findByRole('link', { name: 'AI Insights' })).toHaveAttribute('href', '/insights')
  })

  it.each(['Admin', 'ServiceStaff', 'WarehouseStaff'])('is hidden from and closed to %s', async role => {
    signInAs(role)
    const fetchMock = stubApi({ 'GET /dashboard/summary': { status: 500, body: { title: 'x' } } })
    renderApp('/insights')

    expect(await screen.findByRole('heading', { name: /Welcome back/ })).toBeInTheDocument() // bounced home
    expect(screen.queryByRole('link', { name: 'AI Insights' })).not.toBeInTheDocument()
    expect(callsTo(fetchMock, 'GET', '/ai/manager-insights/topics')).toHaveLength(0)
  })

  it('answers an example question with the figures beside the summary', async () => {
    signInAs('Manager')
    const fetchMock = stubApi({
      'GET /ai/manager-insights/topics': { body: TOPICS },
      'POST /ai/manager-insights': { body: answered() }
    })
    renderApp('/insights')

    await userEvent.click(await screen.findByRole('button', { name: 'How many jobs were completed last month?' }))

    expect(await screen.findByRole('note')).toHaveTextContent('the summary may only repeat them')
    expect(screen.getByText('Jobs completed, Sep 2026: Jobs completed in Sep 2026 — 7 jobs.')).toBeInTheDocument()
    const figures = screen.getByRole('table')
    expect(within(figures).getByRole('row', { name: /Jobs completed in Sep 2026/ })).toHaveTextContent('7 jobs')
    expect(screen.getByRole('heading', { name: /The figures/ })).toHaveTextContent('Sep–Oct 2026')
    expect(screen.getByText(/How it is counted:/)).toBeInTheDocument()
    expect(screen.getByText(/Demo mock assistant/)).toBeInTheDocument()
    expect(screen.getByLabelText('Your question')).toHaveValue('How many jobs were completed last month?')
    const [, init] = callsTo(fetchMock, 'POST', '/ai/manager-insights')[0]
    expect(JSON.parse(String(init?.body))).toEqual({ question: 'How many jobs were completed last month?' })
  })

  it('shows the figures when the summary is unavailable, money as RM and missing values as no data', async () => {
    signInAs('Manager')
    stubApi({
      'GET /ai/manager-insights/topics': { body: TOPICS },
      'POST /ai/manager-insights': {
        body: answered({
          topic: { ...TOPICS[0], id: 'top-parts', title: 'Parts usage' },
          summary: null,
          generatedBy: null,
          summaryUnavailableReason: 'The AI summary is unavailable right now. The figures below come straight from the analytics.',
          figures: [
            { label: 'BRG-6204 Bearing: cost', value: 1234.5, unit: 'RM' },
            { label: 'Average time from start to completion', value: null, unit: 'hours' }
          ]
        })
      }
    })
    renderApp('/insights')

    await userEvent.type(await screen.findByLabelText('Your question'), 'How much did we spend on parts?')
    await userEvent.click(screen.getByRole('button', { name: 'Ask' }))

    expect(await screen.findByRole('status')).toHaveTextContent('The AI summary is unavailable right now')
    expect(screen.getByRole('row', { name: /BRG-6204/ })).toHaveTextContent('RM 1,234.50')
    expect(screen.getByRole('row', { name: /Average time/ })).toHaveTextContent('no data')
  })

  it('explains a refusal and lists what can be asked, each one a click away', async () => {
    signInAs('Manager')
    const fetchMock = stubApi({
      'GET /ai/manager-insights/topics': { body: TOPICS },
      'POST /ai/manager-insights': ({ body }) => (body as { question: string }).question === 'What is our revenue?'
        ? {
            body: answered({
              outcome: 'Refused',
              question: 'What is our revenue?',
              topic: null,
              figures: [],
              summary: null,
              refusalReason: 'Revenue, profit and pay are not recorded in this system’s analytics.',
              supportedTopics: TOPICS
            })
          }
        : { body: answered({ topic: TOPICS[1], question: 'Which parts are low on stock?' }) }
    })
    renderApp('/insights')

    await userEvent.type(await screen.findByLabelText('Your question'), 'What is our revenue?')
    await userEvent.click(screen.getByRole('button', { name: 'Ask' }))

    const refusal = await screen.findByRole('region', { name: "I can't answer that" })
    expect(within(refusal).getByRole('status')).toHaveTextContent('Revenue, profit and pay are not recorded')
    expect(within(refusal).getByText('Low stock')).toBeInTheDocument()
    expect(screen.queryByRole('table')).not.toBeInTheDocument()

    await userEvent.click(within(refusal).getByRole('button', { name: 'Which parts are low on stock?' }))

    expect(await screen.findByRole('heading', { name: 'Low stock' })).toBeInTheDocument()
    expect(callsTo(fetchMock, 'POST', '/ai/manager-insights')).toHaveLength(2)
  })

  it('shows the rate-limit message and keeps the question', async () => {
    signInAs('Manager')
    stubApi({
      'GET /ai/manager-insights/topics': { body: TOPICS },
      'POST /ai/manager-insights': {
        status: 429,
        body: { title: 'Too many requests', detail: 'You have asked the AI assistant too often. Wait a minute and try again.' }
      }
    })
    renderApp('/insights')

    await userEvent.type(await screen.findByLabelText('Your question'), 'Which parts are low on stock?')
    await userEvent.click(screen.getByRole('button', { name: 'Ask' }))

    expect(await screen.findByRole('alert')).toHaveTextContent('Wait a minute and try again.')
    expect(screen.getByLabelText('Your question')).toHaveValue('Which parts are low on stock?')
  })
})

describe('formatFigure', () => {
  it('says 1 job, not 1 jobs, and leaves other units alone', () => {
    expect(formatFigure({ label: 'Awaiting approval', value: 1, unit: 'jobs' })).toBe('1 job')
    expect(formatFigure({ label: 'Active jobs', value: 3, unit: 'jobs' })).toBe('3 jobs')
    expect(formatFigure({ label: 'Belting', value: 1, unit: 'm' })).toBe('1 m')
  })
})
