export interface CurrentUser {
  userId: string
  fullName: string
  email: string
  roles: string[]
}

export interface LoginResult {
  token: string
  expiresAtUtc: string
  userId: string
  fullName: string
  email: string
  roles: string[]
}

export interface Customer {
  id: number
  companyName: string
  contactPerson?: string | null
  phone?: string | null
  email?: string | null
  address?: string | null
  createdAt: string
}

export interface CustomerInput {
  companyName: string
  contactPerson?: string
  phone?: string
  email?: string
  address?: string
}

export type EquipmentStatus = 'Active' | 'Inactive' | 'UnderMaintenance' | 'Retired'

export interface Equipment {
  id: number
  customerId: number
  serialNumber: string
  equipmentType: string
  brand?: string | null
  model?: string | null
  installationDate?: string | null
  status: EquipmentStatus
  location?: string | null
}

export interface EquipmentInput {
  customerId: number
  serialNumber: string
  equipmentType: string
  brand?: string
  model?: string
  installationDate?: string
  location?: string
}

export interface EquipmentUpdateInput {
  serialNumber: string
  equipmentType: string
  brand?: string
  model?: string
  installationDate?: string
  location?: string
  status: EquipmentStatus
}

export const ROLES = ['Admin', 'Manager', 'ServiceStaff', 'Technician', 'WarehouseStaff'] as const
export type RoleName = (typeof ROLES)[number]

export interface UserSummary {
  id: string
  fullName: string
  email: string
  role: string
  isActive: boolean
  createdAt: string
}

export interface CreateUserInput {
  fullName: string
  email: string
  password: string
  role: string
}

export interface UpdateUserInput {
  fullName?: string
  role?: string
  isActive?: boolean
}

export interface ServiceHistoryItem {
  type: string
  id: number
  status: string
  priority: string
  summary: string
  equipmentId: number
  occurredAtUtc: string
}

export interface ServiceHistory {
  items: ServiceHistoryItem[]
}

export const REQUEST_STATUSES = ['New', 'Approved', 'Assigned', 'Rejected', 'Cancelled'] as const
export type ServiceRequestStatus = (typeof REQUEST_STATUSES)[number]

export const REQUEST_PRIORITIES = ['Low', 'Medium', 'High', 'Urgent'] as const
export type RequestPriority = (typeof REQUEST_PRIORITIES)[number]

export interface ServiceRequest {
  id: number
  customerId: number
  customerName: string
  equipmentId: number
  equipmentSerialNumber: string
  equipmentType: string
  problemDescription: string
  priority: RequestPriority
  status: ServiceRequestStatus
  createdByUserId: string
  createdByName: string
  createdAt: string
  approvedByName?: string | null
  approvedAt?: string | null
  rejectedByName?: string | null
  rejectedAt?: string | null
  rejectionReason?: string | null
  cancelledByName?: string | null
  cancelledAt?: string | null
  cancellationReason?: string | null
  workOrderId?: number | null
  rowVersion: string
}

export interface ServiceRequestInput {
  customerId: number
  equipmentId: number
  problemDescription: string
  priority: RequestPriority
}

export interface ServiceRequestFilters {
  status?: ServiceRequestStatus | ''
  priority?: RequestPriority | ''
  customerId?: number | ''
}

export const WORK_ORDER_STATUSES = [
  'New', 'Assigned', 'InProgress', 'PendingParts', 'Completed', 'Approved', 'Cancelled'
] as const
export type WorkOrderStatus = (typeof WORK_ORDER_STATUSES)[number]

export interface WorkOrder {
  id: number
  serviceRequestId: number
  customerId: number
  customerName: string
  equipmentId: number
  equipmentSerialNumber: string
  equipmentType: string
  problemDescription: string
  priority: RequestPriority
  technicianId?: string | null
  technicianName: string
  status: WorkOrderStatus
  assignedAt?: string | null
  startedAt?: string | null
  completedAt?: string | null
  approvedAt?: string | null
  diagnosis?: string | null
  workPerformed?: string | null
  notes?: string | null
  cancelledByName?: string | null
  cancelledAt?: string | null
  cancellationReason?: string | null
  rowVersion: string
}

export interface DiagnosisInput {
  diagnosis: string
  workPerformed: string
  notes?: string
  rowVersion: string
}

export interface WorkOrderFilters {
  status?: WorkOrderStatus | ''
}

export interface Technician {
  id: string
  fullName: string
  openJobCount: number
}

// ---- Phase 6: parts and the inventory ledger ----

export interface Part {
  id: number
  partNumber: string
  name: string
  description?: string | null
  unit: string
  unitCost: number
  quantityOnHand: number
  minimumStockLevel: number
  isActive: boolean
  isLowStock: boolean
  createdAt: string
  rowVersion: string
}

// No quantityOnHand: stock only ever changes through a ledger movement (BR-09).
export interface PartInput {
  partNumber: string
  name: string
  description?: string
  unit: string
  unitCost: number
  minimumStockLevel: number
}

export interface PartUpdateInput extends PartInput {
  isActive: boolean
  rowVersion: string
}

export interface PartFilters {
  search?: string
  includeInactive?: boolean
}

export const TRANSACTION_TYPES = ['Receive', 'Issue', 'Return', 'Adjustment'] as const
export type InventoryTransactionType = (typeof TRANSACTION_TYPES)[number]

/** What the warehouse may raise directly; Issue/Return come only from work-order parts (Phase 7). */
export type StockMovementType = Extract<InventoryTransactionType, 'Receive' | 'Adjustment'>

export interface StockMovementInput {
  partId: number
  transactionType: StockMovementType
  quantity: number
  notes?: string
}

export interface InventoryTransaction {
  id: number
  partId: number
  partNumber: string
  partName: string
  unit: string
  transactionType: InventoryTransactionType
  quantity: number
  quantityOnHandAfter?: number | null
  workOrderId?: number | null
  createdByName: string
  notes?: string | null
  createdAt: string
}

export interface TransactionFilters {
  partId?: number | ''
  type?: InventoryTransactionType | ''
  workOrderId?: number | ''
  /** MYT calendar days, yyyy-mm-dd, inclusive at both ends. */
  fromDate?: string
  toDate?: string
}
