import { apiFetch } from './client'
import type { DiagnosisInput, PendingPartsInput, WorkOrder, WorkOrderFilters } from '../types'

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

export function startWorkOrder(token: string, id: number, rowVersion: string): Promise<WorkOrder> {
  return apiFetch<WorkOrder>(`/work-orders/${id}/start`, {
    method: 'POST',
    token,
    body: JSON.stringify({ rowVersion })
  })
}

export function saveDiagnosis(token: string, id: number, input: DiagnosisInput): Promise<WorkOrder> {
  return apiFetch<WorkOrder>(`/work-orders/${id}/diagnosis`, {
    method: 'PATCH',
    token,
    body: JSON.stringify(input)
  })
}

export function completeWorkOrder(token: string, id: number, rowVersion: string): Promise<WorkOrder> {
  return apiFetch<WorkOrder>(`/work-orders/${id}/complete`, {
    method: 'POST',
    token,
    body: JSON.stringify({ rowVersion })
  })
}

export function approveWorkOrder(token: string, id: number, rowVersion: string): Promise<WorkOrder> {
  return apiFetch<WorkOrder>(`/work-orders/${id}/approve`, {
    method: 'POST',
    token,
    body: JSON.stringify({ rowVersion })
  })
}

// ---- Phase 7: parts on a job (assigned technician, InProgress only) and PendingParts ----

// No price is sent: the server snapshots the catalogue price (the client is never trusted with it).
export function addWorkOrderPart(
  token: string, id: number, partId: number, quantity: number, rowVersion: string
): Promise<WorkOrder> {
  return apiFetch<WorkOrder>(`/work-orders/${id}/parts`, {
    method: 'POST',
    token,
    body: JSON.stringify({ partId, quantity, rowVersion })
  })
}

// Returns the whole line to stock.
export function returnWorkOrderPart(token: string, id: number, lineId: number, rowVersion: string): Promise<WorkOrder> {
  return apiFetch<WorkOrder>(`/work-orders/${id}/parts/${lineId}`, {
    method: 'DELETE',
    token,
    body: JSON.stringify({ rowVersion })
  })
}

export function markPendingParts(token: string, id: number, input: PendingPartsInput): Promise<WorkOrder> {
  return apiFetch<WorkOrder>(`/work-orders/${id}/pending-parts`, {
    method: 'POST',
    token,
    body: JSON.stringify(input)
  })
}

// Refused (409) until the awaited part has the awaited quantity on hand.
export function resumeWorkOrder(token: string, id: number, rowVersion: string): Promise<WorkOrder> {
  return apiFetch<WorkOrder>(`/work-orders/${id}/resume`, {
    method: 'POST',
    token,
    body: JSON.stringify({ rowVersion })
  })
}

// Manager only, mirroring approve (plan's demo script: "a Manager cancels an assigned job").
// Reverts the linked ServiceRequest to Approved so it can be reassigned.
export function cancelWorkOrder(token: string, id: number, reason: string, rowVersion: string): Promise<WorkOrder> {
  return apiFetch<WorkOrder>(`/work-orders/${id}/cancel`, {
    method: 'POST',
    token,
    body: JSON.stringify({ reason, rowVersion })
  })
}
