import { screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it } from 'vitest'
import type { DashboardSummary } from '../types'
import { callsTo, renderApp, signInAs, stubApi } from './utils'

function summary(overrides: Partial<DashboardSummary> = {}): DashboardSummary {
  return {
    generatedAt: '2026-09-29T02:00:00',
    kpis: { openRequests: 5, activeWorkOrders: 3, awaitingApproval: 1, completedThisMonth: 2, lowStockParts: 2 },
    openRequestsByPriority: [
      { key: 'Urgent', count: 1 }, { key: 'High', count: 2 }, { key: 'Medium', count: 2 }, { key: 'Low', count: 0 }
    ],
    workOrdersByStatus: [
      { key: 'New', count: 0 }, { key: 'Assigned', count: 1 }, { key: 'InProgress', count: 1 },
      { key: 'PendingParts', count: 1 }, { key: 'Completed', count: 1 }, { key: 'Approved', count: 9 },
      { key: 'Cancelled', count: 1 }
    ],
    jobsCompletedPerMonth: [
      { month: '2026-04', count: 0 }, { month: '2026-05', count: 1 }, { month: '2026-06', count: 2 },
      { month: '2026-07', count: 1 }, { month: '2026-08', count: 2 }, { month: '2026-09', count: 3 }
    ],
    lowStock: {
      count: 2,
      outOfStock: 1,
      items: [
        { partId: 1, partNumber: 'BLT-CNV-600', name: 'Conveyor belt, 600 mm', unit: 'm', quantityOnHand: 0, minimumStockLevel: 10 },
        { partId: 2, partNumber: 'AVR-SX460', name: 'Automatic voltage regulator', unit: 'pcs', quantityOnHand: 1, minimumStockLevel: 1 }
      ]
    },
    ...overrides
  }
}

function tile(label: string) {
  return screen.getByText(label).closest('a')!
}

describe('Manager dashboard (Phase 11)', () => {
  it('shows the server-computed tiles and charts to a Manager', async () => {
    signInAs('Manager', 'Jenny Lau')
    stubApi({ 'GET /dashboard/summary': { body: summary() } })
    renderApp('/')

    expect(await screen.findByText('Open requests')).toBeInTheDocument()
    expect(within(tile('Open requests')).getByText('5')).toBeInTheDocument()
    expect(within(tile('Active jobs')).getByText('3')).toBeInTheDocument()
    expect(within(tile('Awaiting your approval')).getByText('1')).toBeInTheDocument()
    expect(within(tile('Completed this month')).getByText('2')).toBeInTheDocument()
    expect(tile('Parts low on stock')).toHaveAttribute('href', '/inventory')

    // Charts are tables and lists underneath, so every number is readable, labelled text.
    const priorities = screen.getByRole('table', { name: /by priority/ })
    expect(within(priorities).getByRole('rowheader', { name: 'Urgent' }).closest('tr')).toHaveTextContent('Urgent1')
    const statuses = screen.getByRole('table', { name: 'All work orders by status' })
    expect(within(statuses).getByRole('rowheader', { name: 'Pending Parts' })).toBeInTheDocument()
    expect(within(statuses).getByRole('rowheader', { name: 'Approved' }).closest('tr')).toHaveTextContent('Approved9')

    const months = screen.getByRole('list', { name: /Jobs completed per month/ })
    expect(within(months).getAllByRole('listitem')).toHaveLength(6)
    expect(within(months).getByText('Sep 2026: 3 jobs completed')).toBeInTheDocument()
    expect(within(months).getByText('May 2026: 1 job completed')).toBeInTheDocument()

    expect(screen.getByText(/2 active parts are at or below the minimum/)).toHaveTextContent('1 out of stock')
    expect(screen.getByText('BLT-CNV-600').closest('tr')).toHaveClass('row-warn')
  })

  it('shows empty charts as sentences, not blank bars', async () => {
    signInAs('Admin')
    stubApi({
      'GET /dashboard/summary': {
        body: summary({
          kpis: { openRequests: 0, activeWorkOrders: 0, awaitingApproval: 0, completedThisMonth: 0, lowStockParts: 0 },
          openRequestsByPriority: summary().openRequestsByPriority.map(c => ({ ...c, count: 0 })),
          workOrdersByStatus: summary().workOrdersByStatus.map(c => ({ ...c, count: 0 })),
          jobsCompletedPerMonth: summary().jobsCompletedPerMonth.map(m => ({ ...m, count: 0 })),
          lowStock: { count: 0, outOfStock: 0, items: [] }
        })
      }
    })
    renderApp('/')

    expect(await screen.findByText('No open requests: everything has been reviewed and assigned.')).toBeInTheDocument()
    expect(screen.getByText('No work orders yet.')).toBeInTheDocument()
    expect(screen.getByText('No jobs completed in the last six months.')).toBeInTheDocument()
    expect(screen.getByText('Every active part is above its minimum stock level.')).toBeInTheDocument()
  })

  it('reports a failure and recovers on Refresh', async () => {
    signInAs('Manager')
    let fail = true
    const fetchMock = stubApi({
      'GET /dashboard/summary': () =>
        fail ? { status: 500, body: { title: 'Server error', detail: 'Something went wrong.' } } : { body: summary() }
    })
    renderApp('/')

    expect(await screen.findByRole('alert')).toBeInTheDocument()
    fail = false
    await userEvent.click(screen.getByRole('button', { name: 'Refresh' }))

    expect(await screen.findByText('Awaiting your approval')).toBeInTheDocument()
    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
    expect(callsTo(fetchMock, 'GET', '/dashboard/summary')).toHaveLength(2)
  })

  it('is not requested for ServiceStaff, who keep the simple counts', async () => {
    signInAs('ServiceStaff')
    const fetchMock = stubApi({
      'GET /customers': { body: [] },
      'GET /equipment': { body: [] }
    })
    renderApp('/')

    expect(await screen.findByText('Customers', { selector: '.stat-label' })).toBeInTheDocument()
    expect(callsTo(fetchMock, 'GET', '/dashboard/summary')).toHaveLength(0)
    expect(screen.queryByText('Open requests')).not.toBeInTheDocument()
  })
})
