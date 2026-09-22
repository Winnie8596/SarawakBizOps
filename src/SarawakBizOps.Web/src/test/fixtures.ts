import type { Customer, Equipment, ServiceRequest, Technician, WorkOrder } from '../types'

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
    cancelledByName: null,
    cancelledAt: null,
    cancellationReason: null,
    rowVersion: 'AAAAAAAAB9E=',
    ...overrides
  }
}
