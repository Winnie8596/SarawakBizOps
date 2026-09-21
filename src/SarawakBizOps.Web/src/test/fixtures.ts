import type { Customer, Equipment, ServiceRequest } from '../types'

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
    ...overrides
  }
}
