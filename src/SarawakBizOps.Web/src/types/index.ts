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
