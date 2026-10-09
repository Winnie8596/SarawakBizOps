import type {
  CursorPage,
  Customer,
  Equipment,
  InventoryTransaction,
  ManagerDashboard,
  PagedResult,
  Part,
  ServiceQueueDashboard,
  ServiceRequest,
  Technician,
  WarehouseDashboard,
  WorkOrder,
  WorkOrderPart,
  WorkOrderPhoto
} from '../types'

// The API sends every property, a missing value as null.
const noContact = { contactPerson: null, phone: null, email: null, address: null }
const noDetails = { brand: null, model: null, installationDate: null, location: null }

export const customers: Customer[] = [
  { id: 1, companyName: 'Kuching Cold Chain Sdn Bhd', createdAt: '2026-09-01T00:00:00', ...noContact },
  { id: 2, companyName: 'Rajang Timber Mills Sdn Bhd', createdAt: '2026-09-01T00:00:00', ...noContact }
]

export const rajangEquipment: Equipment[] = [
  { id: 20, customerId: 2, serialNumber: 'RTM-BSW-001', equipmentType: 'Band Saw', status: 'Active', ...noDetails },
  {
    id: 21,
    customerId: 2,
    serialNumber: 'RTM-CNV-003',
    equipmentType: 'Log Conveyor',
    status: 'Inactive',
    ...noDetails
  },
  { id: 22, customerId: 2, serialNumber: 'RTM-OLD-009', equipmentType: 'Old Planer', status: 'Retired', ...noDetails }
]

export function serviceRequest(overrides: Partial<ServiceRequest> = {}): ServiceRequest {
  return {
    id: 7,
    customerId: 2,
    customerName: 'Rajang Timber Mills Sdn Bhd',
    equipmentId: 20,
    equipmentSerialNumber: 'RTM-BSW-001',
    equipmentType: 'Band Saw',
    problemDescription: 'Blade tracking drifts after warm-up.',
    priority: 'High',
    status: 'New',
    createdByUserId: 'staff-id',
    createdByName: 'Aina Abdullah',
    createdAt: '2026-09-20T02:30:00',
    approvedByName: null,
    approvedAt: null,
    rejectedByName: null,
    rejectedAt: null,
    rejectionReason: null,
    cancelledByName: null,
    cancelledAt: null,
    cancellationReason: null,
    workOrderId: null,
    cancelledWorkOrderIds: [],
    rowVersion: 'AAAAAAAAB9E=',
    ...overrides
  }
}

export const technicians: Technician[] = [
  { id: 'tech-1', fullName: 'Ahmad Bujang', openJobCount: 0 },
  { id: 'tech-2', fullName: 'Grace Anak Jimbun', openJobCount: 2 }
]

export function workOrder(overrides: Partial<WorkOrder> = {}): WorkOrder {
  return {
    id: 41,
    serviceRequestId: 7,
    customerId: 2,
    customerName: 'Rajang Timber Mills Sdn Bhd',
    equipmentId: 20,
    equipmentSerialNumber: 'RTM-BSW-001',
    equipmentType: 'Band Saw',
    problemDescription: 'Blade tracking drifts after warm-up.',
    priority: 'High',
    technicianId: 'tech-1',
    technicianName: 'Ahmad Bujang',
    status: 'Assigned',
    assignedAt: '2026-09-20T02:30:00',
    startedAt: null,
    completedAt: null,
    approvedAt: null,
    approvedByName: null,
    diagnosis: null,
    workPerformed: null,
    notes: null,
    hasSignature: false,
    signedAt: null,
    photos: [],
    report: null,
    cancelledByName: null,
    cancelledAt: null,
    cancellationReason: null,
    parts: [],
    partsTotal: 0,
    pendingPartsReason: null,
    pendingPartId: null,
    pendingPartNumber: null,
    pendingPartName: null,
    pendingPartUnit: null,
    pendingPartQuantity: null,
    pendingPartsAt: null,
    resumedAt: null,
    rowVersion: 'AAAAAAAAB9E=',
    ...overrides
  }
}

export function workOrderPhoto(overrides: Partial<WorkOrderPhoto> = {}): WorkOrderPhoto {
  return {
    id: 801,
    contentType: 'image/jpeg',
    sizeBytes: 245_760,
    uploadedByName: 'Ahmad Bujang',
    uploadedAt: '2026-09-21T01:30:00',
    ...overrides
  }
}

export function workOrderPart(overrides: Partial<WorkOrderPart> = {}): WorkOrderPart {
  return {
    id: 501,
    partId: 301,
    partNumber: 'REL-OVL-25A',
    partName: 'Thermal overload relay 18–25 A',
    unit: 'pcs',
    quantity: 2,
    unitPrice: 1450.5,
    lineTotal: 2901,
    createdAt: '2026-09-21T01:30:00',
    ...overrides
  }
}

export function part(overrides: Partial<Part> = {}): Part {
  return {
    id: 301,
    partNumber: 'REL-OVL-25A',
    name: 'Thermal overload relay 18–25 A',
    description: null,
    unit: 'pcs',
    unitCost: 1450.5,
    quantityOnHand: 6,
    minimumStockLevel: 2,
    isActive: true,
    isLowStock: false,
    createdAt: '2026-09-01T00:00:00',
    rowVersion: 'AAAAAAAAC9E=',
    ...overrides
  }
}

export function inventoryTransaction(overrides: Partial<InventoryTransaction> = {}): InventoryTransaction {
  return {
    id: 901,
    partId: 301,
    partNumber: 'REL-OVL-25A',
    partName: 'Thermal overload relay 18–25 A',
    unit: 'pcs',
    transactionType: 'Receive',
    quantity: 6,
    quantityOnHandAfter: null,
    workOrderId: null,
    createdByName: 'Siti Nurul',
    notes: 'Delivery note DN-24817',
    createdAt: '2026-09-20T02:30:00',
    ...overrides
  }
}

/** GET /parts answers with one page of the catalogue. */
export function paged<T>(items: T[], overrides: Partial<PagedResult<T>> = {}): PagedResult<T> {
  return { items, totalCount: items.length, page: 1, pageSize: 50, ...overrides }
}

/** GET /inventory-transactions answers with one keyset page; null nextCursor means it was the last. */
export function cursorPage<T>(items: T[], nextCursor: string | null = null): CursorPage<T> {
  return { items, nextCursor }
}

// ---- The three dashboards (GET /api/dashboard/summary), as the demo data would give them ----

const period = {
  fromMonth: '2026-04',
  toMonth: '2026-09',
  fromUtc: '2026-03-31T16:00:00',
  toUtc: '2026-09-30T16:00:00'
}

export function managerDashboard(overrides: Partial<ManagerDashboard> = {}): ManagerDashboard {
  return {
    view: 'Manager',
    generatedAt: '2026-09-29T02:00:00',
    period,
    kpis: { openRequests: 5, activeWorkOrders: 3, awaitingApproval: 1, completedThisMonth: 2, lowStockParts: 2 },
    openRequestsByPriority: [
      { key: 'Urgent', count: 1 },
      { key: 'High', count: 2 },
      { key: 'Medium', count: 2 },
      { key: 'Low', count: 0 }
    ],
    workOrdersByStatus: [
      { key: 'Assigned', count: 1 },
      { key: 'InProgress', count: 1 },
      { key: 'PendingParts', count: 1 },
      { key: 'Completed', count: 1 },
      { key: 'Approved', count: 9 },
      { key: 'Cancelled', count: 1 }
    ],
    jobsCompletedPerMonth: [
      { month: '2026-04', count: 0 },
      { month: '2026-05', count: 1 },
      { month: '2026-06', count: 2 },
      { month: '2026-07', count: 1 },
      { month: '2026-08', count: 2 },
      { month: '2026-09', count: 3 }
    ],
    lowStock: {
      count: 2,
      outOfStock: 1,
      items: [
        {
          partId: 1,
          partNumber: 'BLT-CNV-600',
          name: 'Conveyor belt, 600 mm',
          unit: 'm',
          quantityOnHand: 0,
          minimumStockLevel: 10
        },
        {
          partId: 2,
          partNumber: 'AVR-SX460',
          name: 'Automatic voltage regulator',
          unit: 'pcs',
          quantityOnHand: 1,
          minimumStockLevel: 1
        }
      ]
    },
    averageTimeToAssignment: { averageHours: 47.74, count: 14 },
    averageCompletionDuration: { averageHours: 22.8, count: 10 },
    repeatedFailures: {
      minimumRequests: 3,
      equipmentCount: 2,
      items: [
        {
          equipmentId: 3,
          serialNumber: 'SBW-FIL-001',
          equipmentType: 'Bottle Filling Line',
          customerId: 2,
          customerName: 'Santubong Bottling Works Sdn Bhd',
          requestCount: 3,
          lastReportedAt: '2026-09-27T02:00:00'
        },
        {
          equipmentId: 9,
          serialNumber: 'SPO-PMP-002',
          equipmentType: 'Centrifugal Pump',
          customerId: 4,
          customerName: 'Sibu Palm Oil Processing Sdn Bhd',
          requestCount: 3,
          lastReportedAt: '2026-09-15T02:00:00'
        }
      ]
    },
    topPartsByUsage: [
      {
        partId: 4,
        partNumber: 'SEAL-KIT-HYD',
        name: 'Hydraulic lift-cylinder seal kit',
        unit: 'kit',
        quantity: 1,
        cost: 185,
        jobCount: 1
      },
      {
        partId: 3,
        partNumber: 'OIL-HYD-46',
        name: 'Hydraulic oil ISO VG 46',
        unit: 'L',
        quantity: 12,
        cost: 178.8,
        jobCount: 1
      }
    ],
    ...overrides
  }
}

export function warehouseDashboard(overrides: Partial<WarehouseDashboard> = {}): WarehouseDashboard {
  return {
    view: 'Warehouse',
    generatedAt: '2026-09-29T02:00:00',
    period,
    kpis: { activeParts: 11, lowStockParts: 4, outOfStockParts: 1, jobsWaitingForParts: 2 },
    lowStock: managerDashboard().lowStock,
    jobsWaitingForParts: {
      count: 2,
      readyToResume: 1,
      items: [
        {
          workOrderId: 5,
          partId: 11,
          partNumber: 'BLT-CNV-600',
          name: 'Conveyor belt, 600 mm',
          unit: 'm',
          quantityNeeded: 6,
          quantityOnHand: 0,
          waitingSince: '2026-09-26T02:00:00',
          inStock: false
        },
        {
          workOrderId: 7,
          partId: 8,
          partNumber: 'BRG-6205-2RS',
          name: 'Deep-groove ball bearing',
          unit: 'pcs',
          quantityNeeded: 2,
          quantityOnHand: 38,
          waitingSince: '2026-09-27T02:00:00',
          inStock: true
        }
      ]
    },
    topPartsByUsage: managerDashboard().topPartsByUsage,
    ...overrides
  }
}

export function serviceQueueDashboard(overrides: Partial<ServiceQueueDashboard> = {}): ServiceQueueDashboard {
  return {
    view: 'ServiceQueue',
    generatedAt: '2026-09-29T02:00:00',
    kpis: { awaitingReview: 3, awaitingAssignment: 2, rejectedRecently: 1 },
    openRequestsByPriority: managerDashboard().openRequestsByPriority,
    queue: {
      awaitingReview: 3,
      awaitingAssignment: 2,
      recentDays: 30,
      rejectedRecently: 1,
      oldestOpen: [
        {
          id: 5,
          customerName: 'Sibu Palm Oil Processing Sdn Bhd',
          equipmentSerialNumber: 'SPO-PMP-002',
          equipmentType: 'Centrifugal Pump',
          priority: 'High',
          status: 'Approved',
          createdAt: '2026-09-15T02:00:00'
        },
        {
          id: 3,
          customerName: 'Sibu Palm Oil Processing Sdn Bhd',
          equipmentSerialNumber: 'SPO-BLR-001',
          equipmentType: 'Steam Boiler',
          priority: 'Urgent',
          status: 'New',
          createdAt: '2026-09-26T02:00:00'
        }
      ],
      recentlyRejected: [
        {
          id: 6,
          customerName: 'Rajang Timber Mills Sdn Bhd',
          equipmentSerialNumber: 'RTM-CNV-003',
          rejectedAt: '2026-09-10T02:00:00',
          rejectionReason: 'The conveyor is inactive pending replacement.'
        }
      ]
    },
    ...overrides
  }
}
