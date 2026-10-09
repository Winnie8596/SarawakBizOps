import { screen, waitFor } from '@testing-library/react'
import axe from 'axe-core'
import { beforeAll, describe, expect, it } from 'vitest'
import type { ServiceReport, UserSummary } from '../types'
import {
  cursorPage,
  customers,
  inventoryTransaction,
  managerDashboard,
  paged,
  part,
  rajangEquipment,
  serviceQueueDashboard,
  serviceRequest,
  technicians,
  warehouseDashboard,
  workOrder,
  workOrderPart,
  workOrderPhoto
} from './fixtures'
import { renderApp, signInAs, stubApi } from './utils'

// The Phase 15 axe-core sweep (docs/test-plan.md), automated so it cannot silently regress: every page, as the role
// that uses it, with realistic data, against the WCAG 2.1 A and AA rules. jsdom does no layout, so the rules that
// need it (colour contrast) cannot run here; the browser sweep still covers those.
const RULES: axe.RunOptions = {
  runOnly: { type: 'tag', values: ['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa'] },
  rules: { 'color-contrast': { enabled: false } }
}

const history = {
  items: [
    {
      type: 'WorkOrder',
      id: 41,
      status: 'Approved',
      priority: 'High',
      summary: 'Blade tracking drifts after warm-up.',
      equipmentId: 20,
      occurredAtUtc: '2026-09-21T01:30:00Z'
    }
  ]
}

const report: ServiceReport = {
  id: 1,
  workOrderId: 41,
  workOrderStatus: 'Approved',
  reportNumber: 'SR-2026-000001',
  revision: 1,
  generatedAt: '2026-09-22T01:00:00Z',
  generatedByName: 'Jenny Lau',
  sizeBytes: 182_000,
  isLocked: true,
  customerName: 'Rajang Timber Mills Sdn Bhd',
  equipmentSerialNumber: 'RTM-BSW-001',
  equipmentType: 'Band Saw',
  technicianName: 'Ahmad Bujang',
  completedAt: '2026-09-21T06:00:00Z'
}

const users: UserSummary[] = [
  {
    id: 'u1',
    fullName: 'Jenny Lau',
    email: 'manager@sarawakbizops.local',
    role: 'Manager',
    isActive: true,
    createdAt: '2026-09-01T00:00:00Z'
  }
]

const topics = [
  {
    id: 'jobs-completed',
    title: 'Jobs completed',
    description: 'Jobs completed per month (Completed or Approved).',
    exampleQuestion: 'How many jobs were completed last month?'
  }
]

const job = workOrder({
  status: 'InProgress',
  startedAt: '2026-09-21T01:00:00Z',
  parts: [workOrderPart()],
  partsTotal: 2901
})
const photo = new Blob([new Uint8Array([0xff, 0xd8, 0xff, 0xd9])], { type: 'image/jpeg' })

// Every GET a page here makes, answered with fixture data.
function stubEveryRead(dashboard: unknown) {
  stubApi({
    'GET /dashboard/summary': { body: dashboard },
    'GET /customers': { body: customers },
    'GET /customers/2': { body: customers[1] },
    'GET /customers/2/history': { body: history },
    'GET /equipment': { body: rajangEquipment },
    'GET /equipment/20': { body: rajangEquipment[0] },
    'GET /equipment/20/history': { body: history },
    'GET /service-requests': { body: [serviceRequest()] },
    'GET /service-requests/7': { body: serviceRequest() },
    'GET /technicians': { body: technicians },
    'GET /work-orders': { body: [job] },
    'GET /work-orders/mine': { body: [job] },
    'GET /work-orders/41': { body: { ...job, photos: [workOrderPhoto()] } },
    'GET /work-orders/41/photos/801': { body: photo },
    'GET /reports': { body: [report] },
    'GET /parts': { body: paged([part(), part({ id: 302, partNumber: 'BLT-CNV-600', isLowStock: true })]) },
    'GET /parts/low-stock': { body: [part({ isLowStock: true })] },
    'GET /parts/301': { body: part() },
    'GET /inventory-transactions': { body: cursorPage([inventoryTransaction()]) },
    'GET /ai/manager-insights/topics': { body: topics },
    'GET /users': { body: users }
  })
}

const SIGNED_OUT = 'Signed out'

const pages: [role: string, path: string, dashboard?: unknown][] = [
  ['Manager', '/', managerDashboard()],
  ['WarehouseStaff', '/', warehouseDashboard()],
  ['ServiceStaff', '/', serviceQueueDashboard()],
  ['Manager', '/customers'],
  ['Manager', '/customers/2'],
  ['Manager', '/equipment'],
  ['Manager', '/equipment/20'],
  ['Manager', '/service-requests'],
  ['Manager', '/service-requests/7'],
  ['Manager', '/work-orders'],
  ['Manager', '/work-orders/41'],
  ['Manager', '/reports'],
  ['Manager', '/inventory'],
  ['Manager', '/inventory/history'],
  ['Manager', '/insights'],
  ['Manager', '/account/password'],
  ['Admin', '/users'],
  ['Technician', '/tech/jobs'],
  ['Technician', '/tech/jobs/41'],
  [SIGNED_OUT, '/login'],
  [SIGNED_OUT, '/404']
]

describe('Accessibility (axe-core, WCAG 2.1 A/AA)', () => {
  beforeAll(() => {
    // What index.html gives the real page.
    document.documentElement.lang = 'en'
    document.title = 'SarawakBizOps'
  })

  it.each(pages)('%s on %s has no violations', async (role, path, dashboard) => {
    if (role !== SIGNED_OUT) signInAs(role, 'Jenny Lau')
    stubEveryRead(dashboard ?? managerDashboard())
    renderApp(path)

    await screen.findAllByRole('heading')
    await waitFor(() => expect(screen.queryByText(/^Loading/)).not.toBeInTheDocument())
    // The page shows its data, not an error from a read this file forgot to stub.
    expect(screen.queryByText(/No stub for/)).not.toBeInTheDocument()

    const { violations } = await axe.run(document, RULES)
    expect(violations.map(v => `${v.id}: ${v.nodes.map(n => n.target.join(' ')).join(', ')}`)).toEqual([])
  })
})
