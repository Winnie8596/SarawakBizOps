import { apiFetch } from './client'
import type { Equipment, EquipmentInput, EquipmentUpdateInput, ServiceHistory } from '../types'

export function getEquipment(token: string, customerId?: number): Promise<Equipment[]> {
  const query = customerId ? `?customerId=${customerId}` : ''
  return apiFetch<Equipment[]>(`/equipment${query}`, { token })
}

export function getEquipmentById(token: string, id: number): Promise<Equipment> {
  return apiFetch<Equipment>(`/equipment/${id}`, { token })
}

export function createEquipment(token: string, input: EquipmentInput): Promise<Equipment> {
  return apiFetch<Equipment>('/equipment', {
    method: 'POST',
    token,
    body: JSON.stringify(input)
  })
}

export function updateEquipment(token: string, id: number, input: EquipmentUpdateInput): Promise<Equipment> {
  return apiFetch<Equipment>(`/equipment/${id}`, {
    method: 'PUT',
    token,
    body: JSON.stringify(input)
  })
}

export function getEquipmentHistory(token: string, id: number): Promise<ServiceHistory> {
  return apiFetch<ServiceHistory>(`/equipment/${id}/history`, { token })
}
