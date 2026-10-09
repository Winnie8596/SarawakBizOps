import { apiFetch } from './client'
import type { Equipment, EquipmentInput, EquipmentUpdateInput, ServiceHistory } from '../types'

export function getEquipment(customerId?: number): Promise<Equipment[]> {
  const query = customerId ? `?customerId=${customerId}` : ''
  return apiFetch<Equipment[]>(`/equipment${query}`)
}

export function getEquipmentById(id: number): Promise<Equipment> {
  return apiFetch<Equipment>(`/equipment/${id}`)
}

export function createEquipment(input: EquipmentInput): Promise<Equipment> {
  return apiFetch<Equipment>('/equipment', {
    method: 'POST',
    body: JSON.stringify(input)
  })
}

export function updateEquipment(id: number, input: EquipmentUpdateInput): Promise<Equipment> {
  return apiFetch<Equipment>(`/equipment/${id}`, {
    method: 'PUT',
    body: JSON.stringify(input)
  })
}

export function getEquipmentHistory(id: number): Promise<ServiceHistory> {
  return apiFetch<ServiceHistory>(`/equipment/${id}/history`)
}
