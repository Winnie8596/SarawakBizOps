import { apiFetch } from './client'
import type { Equipment, EquipmentInput } from '../types'

export function getEquipment(token: string, customerId?: number): Promise<Equipment[]> {
  const query = customerId ? `?customerId=${customerId}` : ''
  return apiFetch<Equipment[]>(`/equipment${query}`, { token })
}

export function createEquipment(token: string, input: EquipmentInput): Promise<Equipment> {
  return apiFetch<Equipment>('/equipment', {
    method: 'POST',
    token,
    body: JSON.stringify(input)
  })
}
