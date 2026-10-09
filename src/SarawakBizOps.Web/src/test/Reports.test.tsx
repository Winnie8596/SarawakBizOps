import { screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { ServiceReport, ServiceReportSummary } from '../types'
import { workOrder } from './fixtures'
import { callsTo, renderApp, signInAs, stubApi } from './utils'

const pdf = new Blob(['%PDF-1.4 test'], { type: 'application/pdf' })

function reportSummary(overrides: Partial<ServiceReportSummary> = {}): ServiceReportSummary {
  return {
    reportNumber: 'SR-2026-000123',
    revision: 1,
    generatedAt: '2026-09-22T02:05:00',
    generatedByName: 'Jenny Lau',
    sizeBytes: 24_576,
    isLocked: false,
    ...overrides
  }
}

function reportRow(overrides: Partial<ServiceReport> = {}): ServiceReport {
  return {
    ...reportSummary(),
    id: 1,
    workOrderId: 41,
    workOrderStatus: 'Completed',
    customerName: 'Rajang Timber Mills Sdn Bhd',
    equipmentSerialNumber: 'RTM-BSW-001',
    equipmentType: 'Band Saw',
    technicianName: 'Ahmad Bujang',
    completedAt: '2026-09-21T08:00:00',
    ...overrides
  }
}

const completed = {
  status: 'Completed' as const,
  diagnosis: 'Loose belt',
  workPerformed: 'Retensioned it',
  completedAt: '2026-09-21T08:00:00'
}

let clicked: HTMLAnchorElement[]

beforeEach(() => {
  clicked = []
  // jsdom cannot follow a download link; record what would have been saved instead.
  vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(function (this: HTMLAnchorElement) {
    clicked.push(this)
  })
})

afterEach(() => {
  vi.restoreAllMocks()
})

describe('Service report on the work order (office)', () => {
  it('lets a Manager generate the report of a completed job, then offers regenerate and the PDF', async () => {
    signInAs('Manager')
    const fetchMock = stubApi({
      'GET /work-orders/41': { body: workOrder(completed) },
      'POST /work-orders/41/generate-report': { body: workOrder({ ...completed, report: reportSummary() }) }
    })
    renderApp('/work-orders/41')

    expect(
      await screen.findByText('No report yet. Generate it once you have checked the completed job.')
    ).toBeInTheDocument()
    await userEvent.click(screen.getByRole('button', { name: 'Generate report' }))

    expect(await screen.findByText('SR-2026-000123')).toBeInTheDocument()
    expect(screen.getByText('24 KB')).toBeInTheDocument()
    expect(screen.getByText(/by Jenny Lau/)).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Regenerate report' })).toBeEnabled()
    expect(screen.getByRole('button', { name: 'Download PDF' })).toBeInTheDocument()
    // Generating documents the job without changing it: no body, no rowVersion.
    const [, init] = callsTo(fetchMock, 'POST', '/work-orders/41/generate-report')[0]
    expect(init?.body).toBeUndefined()
    // Approval is still the Manager's next step.
    expect(screen.getByRole('button', { name: 'Approve' })).toBeEnabled()
  })

  it('downloads the PDF with the bearer token and saves it under the report number', async () => {
    signInAs('Manager')
    const fetchMock = stubApi({
      'GET /work-orders/41': { body: workOrder({ ...completed, report: reportSummary() }) },
      'GET /work-orders/41/report': { body: pdf }
    })
    renderApp('/work-orders/41')

    await userEvent.click(await screen.findByRole('button', { name: 'Download PDF' }))

    await vi.waitFor(() => expect(clicked).toHaveLength(1))
    expect(clicked[0].download).toBe('SR-2026-000123.pdf')
    const [, init] = callsTo(fetchMock, 'GET', '/work-orders/41/report')[0]
    expect(new Headers(init?.headers).get('Authorization')).toBe('Bearer test-token')
  })

  it('opens the PDF in the tab it opened on the click', async () => {
    signInAs('Manager')
    stubApi({
      'GET /work-orders/41': { body: workOrder({ ...completed, report: reportSummary() }) },
      'GET /work-orders/41/report': { body: pdf }
    })
    const tab = { closed: false, close: vi.fn(), location: { href: '' } }
    const open = vi.spyOn(window, 'open').mockReturnValue(tab as unknown as Window)
    renderApp('/work-orders/41')

    await userEvent.click(await screen.findByRole('button', { name: 'Open PDF' }))

    expect(open).toHaveBeenCalledWith('', '_blank')
    await vi.waitFor(() => expect(tab.location.href).toBe('blob:test-object-url'))
    expect(clicked).toHaveLength(0)
  })

  it('shows an approved job’s report as locked, without regenerate', async () => {
    signInAs('Manager')
    stubApi({
      'GET /work-orders/41': {
        body: workOrder({
          ...completed,
          status: 'Approved',
          approvedAt: '2026-09-22T03:00:00',
          report: reportSummary({ revision: 2, isLocked: true })
        })
      }
    })
    renderApp('/work-orders/41')

    expect(
      await screen.findByText('Locked — the job is approved, so this report can no longer be regenerated.')
    ).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /Generate report|Regenerate report/ })).not.toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Download PDF' })).toBeInTheDocument()
  })

  it('offers the one report an approved job without a report may still get', async () => {
    signInAs('Manager')
    stubApi({
      'GET /work-orders/41': {
        body: workOrder({ ...completed, status: 'Approved', approvedAt: '2026-09-22T03:00:00' })
      }
    })
    renderApp('/work-orders/41')

    expect(await screen.findByRole('button', { name: 'Generate report' })).toBeEnabled()
  })

  it('explains a refused generation and reloads the job', async () => {
    signInAs('Manager')
    let loads = 0
    stubApi({
      'GET /work-orders/41': () => {
        loads += 1
        return loads === 1
          ? { body: workOrder(completed) }
          : { body: workOrder({ ...completed, status: 'Approved', report: reportSummary({ isLocked: true }) }) }
      },
      'POST /work-orders/41/generate-report': {
        status: 409,
        body: {
          title: 'Conflict',
          detail: 'This job is Approved, so its service report SR-2026-000123 is locked and cannot be regenerated.'
        }
      }
    })
    renderApp('/work-orders/41')

    await userEvent.click(await screen.findByRole('button', { name: 'Generate report' }))

    expect(await screen.findByText(/is locked and cannot be regenerated/)).toBeInTheDocument()
    expect(
      await screen.findByText('Locked — the job is approved, so this report can no longer be regenerated.')
    ).toBeInTheDocument()
  })

  it('lets ServiceStaff download a report but not generate one', async () => {
    signInAs('ServiceStaff')
    stubApi({ 'GET /work-orders/41': { body: workOrder({ ...completed, report: reportSummary() }) } })
    renderApp('/work-orders/41')

    expect(await screen.findByRole('button', { name: 'Download PDF' })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /Generate report|Regenerate report/ })).not.toBeInTheDocument()
  })

  it('tells ServiceStaff who makes the report while there is none, and shows no panel before completion', async () => {
    signInAs('ServiceStaff')
    stubApi({ 'GET /work-orders/41': { body: workOrder(completed) } })
    const { unmount } = renderApp('/work-orders/41')
    expect(
      await screen.findByText('No report yet. A Manager generates it once the job is completed.')
    ).toBeInTheDocument()
    unmount()

    stubApi({ 'GET /work-orders/41': { body: workOrder({ status: 'InProgress' }) } })
    renderApp('/work-orders/41')
    await screen.findByRole('heading', { name: /Work order #41/ })
    expect(screen.queryByRole('heading', { name: 'Service report' })).not.toBeInTheDocument()
  })
})

describe('Reports page', () => {
  it('lists every report with its job, state and a download', async () => {
    signInAs('Manager')
    const fetchMock = stubApi({
      'GET /reports': {
        body: [
          reportRow({ id: 2, reportNumber: 'SR-2026-000124', workOrderId: 42, revision: 2 }),
          reportRow({ id: 1, workOrderStatus: 'Approved', isLocked: true })
        ]
      },
      'GET /work-orders/41/report': { body: pdf }
    })
    renderApp('/reports')

    const rows = within(await screen.findByRole('table'))
      .getAllByRole('row')
      .slice(1)
    expect(rows).toHaveLength(2)
    expect(within(rows[0]).getByText('SR-2026-000124')).toBeInTheDocument()
    expect(within(rows[0]).getByText('· rev 2')).toBeInTheDocument()
    expect(within(rows[0]).getByRole('link', { name: '#42' })).toHaveAttribute('href', '/work-orders/42')
    expect(within(rows[0]).getByText('Completed')).toBeInTheDocument()
    expect(within(rows[1]).getByText('Locked')).toBeInTheDocument()
    expect(within(rows[1]).getByText('Rajang Timber Mills Sdn Bhd')).toBeInTheDocument()

    await userEvent.click(within(rows[1]).getByRole('button', { name: 'Download SR-2026-000123' }))

    await vi.waitFor(() => expect(clicked).toHaveLength(1))
    expect(clicked[0].download).toBe('SR-2026-000123.pdf')
    expect(callsTo(fetchMock, 'GET', '/work-orders/41/report')).toHaveLength(1)
  })

  it('says when there are no reports yet', async () => {
    signInAs('ServiceStaff')
    stubApi({ 'GET /reports': { body: [] } })
    renderApp('/reports')

    expect(await screen.findByText('No reports yet')).toBeInTheDocument()
  })

  it('shows why the list could not load', async () => {
    signInAs('Admin')
    stubApi({ 'GET /reports': { status: 500, body: { title: 'Server error', detail: 'Something went wrong.' } } })
    renderApp('/reports')

    expect(await screen.findByText('Something went wrong.')).toBeInTheDocument()
    expect(screen.queryByText('No reports yet')).not.toBeInTheDocument()
  })

  it.each(['WarehouseStaff', 'Technician'])('keeps %s out of the Reports page', async role => {
    signInAs(role)
    const fetchMock = stubApi({ 'GET /work-orders/mine': { body: [] }, 'GET /parts/low-stock': { body: [] } })
    renderApp('/reports')

    await vi.waitFor(() => expect(screen.queryByRole('heading', { name: 'Service reports' })).not.toBeInTheDocument())
    expect(callsTo(fetchMock, 'GET', '/reports')).toHaveLength(0)
  })
})

describe('Service report on the technician’s job', () => {
  it('lets the technician view the report of their completed job', async () => {
    signInAs('Technician')
    stubApi({
      'GET /work-orders/41': { body: workOrder({ ...completed, hasSignature: true, report: reportSummary() }) },
      'GET /work-orders/41/signature': { body: new Blob(['sig'], { type: 'image/png' }) },
      'GET /work-orders/41/report': { body: pdf }
    })
    const tab = { closed: false, close: vi.fn(), location: { href: '' } }
    vi.spyOn(window, 'open').mockReturnValue(tab as unknown as Window)
    renderApp('/tech/jobs/41')

    await userEvent.click(await screen.findByRole('button', { name: 'View report SR-2026-000123' }))

    await vi.waitFor(() => expect(tab.location.href).toBe('blob:test-object-url'))
  })

  it('says the Manager makes the report while there is none', async () => {
    signInAs('Technician')
    stubApi({
      'GET /work-orders/41': { body: workOrder({ ...completed, hasSignature: true }) },
      'GET /work-orders/41/signature': { body: new Blob(['sig'], { type: 'image/png' }) }
    })
    renderApp('/tech/jobs/41')

    expect(
      await screen.findByText('The Manager generates the service report after checking the completed job.')
    ).toBeInTheDocument()
  })
})
