import { apiFetch } from './client'
import type { DiagnosisInput, WorkOrder, WorkOrderFilters } from '../types'

export function getWorkOrders(token: string, filters: WorkOrderFilters = {}): Promise<WorkOrder[]> {
  const params = new URLSearchParams()
  if (filters.status) params.set('status', filters.status)

  const query = params.toString()
  return apiFetch<WorkOrder[]>(`/work-orders${query ? `?${query}` : ''}`, { token })
}

export function getMyWorkOrders(token: string): Promise<WorkOrder[]> {
  return apiFetch<WorkOrder[]>('/work-orders/mine', { token })
}

export function getWorkOrder(token: string, id: number): Promise<WorkOrder> {
  return apiFetch<WorkOrder>(`/work-orders/${id}`, { token })
}

export function startWorkOrder(token: string, id: number): Promise<WorkOrder> {
  return apiFetch<WorkOrder>(`/work-orders/${id}/start`, { method: 'POST', token })
}

export function saveDiagnosis(token: string, id: number, input: DiagnosisInput): Promise<WorkOrder> {
  return apiFetch<WorkOrder>(`/work-orders/${id}/diagnosis`, {
    method: 'PATCH',
    token,
    body: JSON.stringify(input)
  })
}

export function completeWorkOrder(token: string, id: number): Promise<WorkOrder> {
  return apiFetch<WorkOrder>(`/work-orders/${id}/complete`, { method: 'POST', token })
}

export function approveWorkOrder(token: string, id: number): Promise<WorkOrder> {
  return apiFetch<WorkOrder>(`/work-orders/${id}/approve`, { method: 'POST', token })
}
