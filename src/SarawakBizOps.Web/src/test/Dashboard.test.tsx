import { screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it } from 'vitest'
import {
  managerDashboard as summary,
  serviceQueueDashboard as serviceQueue,
  warehouseDashboard as warehouse
} from './fixtures'
import { callsTo, renderApp, signInAs, stubApi } from './utils'

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
})

describe('Analytics depth and role dashboards (Phase 12)', () => {
  it('shows the Manager the turnaround averages, repeated failures and top parts', async () => {
    signInAs('Manager')
    stubApi({ 'GET /dashboard/summary': { body: summary() } })
    renderApp('/')

    const turnaround = await screen.findByRole('region', { name: 'Turnaround, Apr–Sep 2026' })
    expect(within(turnaround).getByText('Request raised → technician assigned').closest('.metric')).toHaveTextContent(
      '47.7 hover 14 assignments'
    )
    expect(within(turnaround).getByText('Job started → completed').closest('.metric')).toHaveTextContent(
      '22.8 hover 10 completed jobs'
    )

    const failures = screen.getByRole('region', { name: 'Repeated failures, Apr–Sep 2026' })
    expect(within(failures).getByText(/2 pieces of equipment with 3 or more/)).toBeInTheDocument()
    const filler = within(failures).getByRole('link', { name: 'SBW-FIL-001' })
    expect(filler).toHaveAttribute('href', '/equipment/3')
    expect(filler.closest('tr')).toHaveTextContent('Bottle Filling Line · Santubong Bottling Works Sdn Bhd3')

    const parts = screen.getByRole('region', { name: 'Top parts by usage, Apr–Sep 2026' })
    const rows = within(parts).getAllByRole('row').slice(1)
    expect(rows.map(r => r.querySelector('.mono')?.textContent)).toEqual(['SEAL-KIT-HYD', 'OIL-HYD-46'])
    expect(rows[1]).toHaveTextContent('12 L')
    expect(rows[1]).toHaveTextContent('RM 178.80')
  })

  it('says so when there is nothing to measure yet', async () => {
    signInAs('Admin')
    stubApi({
      'GET /dashboard/summary': {
        body: summary({
          averageTimeToAssignment: { averageHours: null, count: 0 },
          averageCompletionDuration: { averageHours: null, count: 0 },
          repeatedFailures: { minimumRequests: 3, equipmentCount: 0, items: [] },
          topPartsByUsage: []
        })
      }
    })
    renderApp('/')

    const turnaround = await screen.findByRole('region', { name: /Turnaround/ })
    expect(within(turnaround).getAllByText('nothing to measure yet')).toHaveLength(2)
    expect(screen.getByText('No equipment has had 3 or more fault reports in this period.')).toBeInTheDocument()
    expect(screen.getByText('No parts were used on jobs in Apr–Sep 2026.')).toBeInTheDocument()
  })

  it('gives WarehouseStaff the stock view: tiles, jobs waiting for parts, low stock and usage', async () => {
    signInAs('WarehouseStaff', 'Siti Nurul')
    stubApi({ 'GET /dashboard/summary': { body: warehouse() } })
    renderApp('/')

    expect(await screen.findByText('Jobs waiting for parts', { selector: '.stat-label' })).toBeInTheDocument()
    expect(within(tile('Active parts')).getByText('11')).toBeInTheDocument()
    expect(tile('Out of stock')).toHaveClass('stat-card-warn')

    const waiting = screen.getByRole('table', { name: 'Jobs paused waiting for a part' })
    const [short, ready] = within(waiting).getAllByRole('row').slice(1)
    expect(short).toHaveTextContent('#5')
    expect(short).toHaveTextContent('Short')
    expect(short).toHaveClass('row-warn')
    expect(ready).toHaveTextContent('In stock')
    expect(screen.getByText(/2 jobs are paused for a part; 1 can resume now/)).toBeInTheDocument()
    expect(screen.getByRole('region', { name: 'Low stock' })).toHaveTextContent('BLT-CNV-600')
    expect(screen.getByRole('region', { name: /Top parts by usage/ })).toHaveTextContent('SEAL-KIT-HYD')

    // Not the Manager view.
    expect(screen.queryByText('Awaiting your approval')).not.toBeInTheDocument()
    expect(screen.queryByRole('region', { name: /Turnaround/ })).not.toBeInTheDocument()
  })

  it('gives ServiceStaff their request queue', async () => {
    signInAs('ServiceStaff', 'Aina Abdullah')
    stubApi({ 'GET /dashboard/summary': { body: serviceQueue() } })
    renderApp('/')

    expect(await screen.findByText('Awaiting review')).toBeInTheDocument()
    expect(within(tile('Awaiting review')).getByText('3')).toBeInTheDocument()
    expect(within(tile('Awaiting assignment')).getByText('2')).toBeInTheDocument()
    expect(tile('Rejected in the last 30 days')).toHaveClass('stat-card-warn')

    const oldest = screen.getByRole('table', { name: 'Open requests, the longest-waiting first' })
    const links = within(oldest).getAllByRole('link')
    expect(links.map(l => l.getAttribute('href'))).toEqual(['/service-requests/5', '/service-requests/3'])
    expect(links[1].closest('tr')).toHaveTextContent('Urgent')

    const rejected = screen.getByRole('region', { name: 'Recently rejected' })
    expect(within(rejected).getByRole('link', { name: '#6' })).toHaveAttribute('href', '/service-requests/6')
    expect(rejected).toHaveTextContent('The conveyor is inactive pending replacement.')

    expect(screen.queryByText('Parts low on stock')).not.toBeInTheDocument()
    expect(screen.queryByRole('region', { name: 'Low stock' })).not.toBeInTheDocument()
  })

  it('shows an empty queue as sentences', async () => {
    signInAs('ServiceStaff')
    stubApi({
      'GET /dashboard/summary': {
        body: serviceQueue({
          kpis: { awaitingReview: 0, awaitingAssignment: 0, rejectedRecently: 0 },
          openRequestsByPriority: summary().openRequestsByPriority.map(c => ({ ...c, count: 0 })),
          queue: {
            ...serviceQueue().queue,
            awaitingReview: 0,
            awaitingAssignment: 0,
            rejectedRecently: 0,
            oldestOpen: [],
            recentlyRejected: []
          }
        })
      }
    })
    renderApp('/')

    expect(await screen.findByText('Nothing is waiting for review or assignment.')).toBeInTheDocument()
    expect(screen.getByText('No requests rejected in the last 30 days.')).toBeInTheDocument()
  })

  it('never asks for a dashboard for a Technician, who lands on My Jobs', async () => {
    signInAs('Technician')
    const fetchMock = stubApi({ 'GET /work-orders/mine': { body: [] } })
    renderApp('/')

    expect(await screen.findByRole('heading', { name: 'My Jobs' })).toBeInTheDocument()
    expect(callsTo(fetchMock, 'GET', '/dashboard/summary')).toHaveLength(0)
  })
})
