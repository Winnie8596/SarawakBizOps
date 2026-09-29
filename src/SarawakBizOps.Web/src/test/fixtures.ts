import type {
  Customer, Equipment, InventoryTransaction, Part, ServiceRequest, Technician, WorkOrder, WorkOrderPart, WorkOrderPhoto
} from '../types'

export const customers: Customer[] = [
  { id: 1, companyName: 'Kuching Cold Chain Sdn Bhd', createdAt: '2026-09-01T00:00:00' },
  { id: 2, companyName: 'Rajang Timber Mills Sdn Bhd', createdAt: '2026-09-01T00:00:00' }
]

export const rajangEquipment: Equipment[] = [
  { id: 20, customerId: 2, serialNumber: 'RTM-BSW-001', equipmentType: 'Band Saw', status: 'Active' },
  { id: 21, customerId: 2, serialNumber: 'RTM-CNV-003', equipmentType: 'Log Conveyor', status: 'Inactive' },
  { id: 22, customerId: 2, serialNumber: 'RTM-OLD-009', equipmentType: 'Old Planer', status: 'Retired' }
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
    diagnosis: null,
    workPerformed: null,
    notes: null,
    hasSignature: false,
    signedAt: null,
    photos: [],
    cancelledByName: null,
    cancelledAt: null,
    cancellationReason: null,
    parts: [],
    partsTotal: 0,
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
