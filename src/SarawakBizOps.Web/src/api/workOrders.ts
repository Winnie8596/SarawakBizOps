import { apiFetch, apiFetchBlob, apiUpload } from './client'
import type { DiagnosisInput, PendingPartsInput, WorkOrder, WorkOrderFilters } from '../types'

export function getWorkOrders(filters: WorkOrderFilters = {}): Promise<WorkOrder[]> {
  const params = new URLSearchParams()
  if (filters.status) params.set('status', filters.status)

  const query = params.toString()
  return apiFetch<WorkOrder[]>(`/work-orders${query ? `?${query}` : ''}`)
}

export function getMyWorkOrders(): Promise<WorkOrder[]> {
  return apiFetch<WorkOrder[]>('/work-orders/mine')
}

export function getWorkOrder(id: number): Promise<WorkOrder> {
  return apiFetch<WorkOrder>(`/work-orders/${id}`)
}

export function startWorkOrder(id: number, rowVersion: string): Promise<WorkOrder> {
  return apiFetch<WorkOrder>(`/work-orders/${id}/start`, {
    method: 'POST',
    body: JSON.stringify({ rowVersion })
  })
}

export function saveDiagnosis(id: number, input: DiagnosisInput): Promise<WorkOrder> {
  return apiFetch<WorkOrder>(`/work-orders/${id}/diagnosis`, {
    method: 'PATCH',
    body: JSON.stringify(input)
  })
}

export function completeWorkOrder(id: number, rowVersion: string): Promise<WorkOrder> {
  return apiFetch<WorkOrder>(`/work-orders/${id}/complete`, {
    method: 'POST',
    body: JSON.stringify({ rowVersion })
  })
}

export function approveWorkOrder(id: number, rowVersion: string): Promise<WorkOrder> {
  return apiFetch<WorkOrder>(`/work-orders/${id}/approve`, {
    method: 'POST',
    body: JSON.stringify({ rowVersion })
  })
}

// ---- Phase 7: parts on a job (assigned technician, InProgress only) and PendingParts ----

// No price is sent: the server snapshots the catalogue price (the client is never trusted with it).
export function addWorkOrderPart(id: number, partId: number, quantity: number, rowVersion: string): Promise<WorkOrder> {
  return apiFetch<WorkOrder>(`/work-orders/${id}/parts`, {
    method: 'POST',
    body: JSON.stringify({ partId, quantity, rowVersion })
  })
}

// Returns the whole line to stock.
export function returnWorkOrderPart(id: number, lineId: number, rowVersion: string): Promise<WorkOrder> {
  return apiFetch<WorkOrder>(`/work-orders/${id}/parts/${lineId}`, {
    method: 'DELETE',
    body: JSON.stringify({ rowVersion })
  })
}

export function markPendingParts(id: number, input: PendingPartsInput): Promise<WorkOrder> {
  return apiFetch<WorkOrder>(`/work-orders/${id}/pending-parts`, {
    method: 'POST',
    body: JSON.stringify(input)
  })
}

// Refused (409) until the awaited part has the awaited quantity on hand.
export function resumeWorkOrder(id: number, rowVersion: string): Promise<WorkOrder> {
  return apiFetch<WorkOrder>(`/work-orders/${id}/resume`, {
    method: 'POST',
    body: JSON.stringify({ rowVersion })
  })
}

// ---- Phase 8: the customer's signature (BR-03: required to complete) ----

// multipart/form-data: the PNG from the signature pad plus the job's rowVersion. Assigned technician only,
// while the job is InProgress; signing again replaces the earlier signature.
export function uploadSignature(id: number, png: Blob, rowVersion: string): Promise<WorkOrder> {
  const form = new FormData()
  form.append('file', png, 'signature.png')
  form.append('rowVersion', rowVersion)
  return apiFetch<WorkOrder>(`/work-orders/${id}/signature`, { method: 'POST', body: form })
}

// There is no public URL for the image: it is only ever read through this authorized endpoint.
export function getSignature(id: number): Promise<Blob> {
  return apiFetchBlob(`/work-orders/${id}/signature`)
}

// ---- Phase 9: photos taken on the job (assigned technician, InProgress only) ----

// One photo per request with the job's rowVersion; the queue on the Photos step sends them one after another,
// each with the rowVersion the previous upload returned. Progress is reported as a fraction (0–1).
export function uploadWorkOrderPhoto(
  id: number,
  photo: Blob,
  rowVersion: string,
  onProgress?: (fraction: number) => void
): Promise<WorkOrder> {
  const form = new FormData()
  form.append('file', photo, photo instanceof File ? photo.name : 'photo.jpg')
  form.append('rowVersion', rowVersion)
  return apiUpload<WorkOrder>(`/work-orders/${id}/photos`, { body: form, onProgress })
}

// Only before completion; afterwards the photos are part of the record the Manager approves.
export function removeWorkOrderPhoto(id: number, photoId: number, rowVersion: string): Promise<WorkOrder> {
  return apiFetch<WorkOrder>(`/work-orders/${id}/photos/${photoId}`, {
    method: 'DELETE',
    body: JSON.stringify({ rowVersion })
  })
}

// Like the signature, a photo has no public URL: it is only ever read through this authorized endpoint.
export function getWorkOrderPhoto(id: number, photoId: number): Promise<Blob> {
  return apiFetchBlob(`/work-orders/${id}/photos/${photoId}`)
}

// Manager only, mirroring approve (plan's demo script: "a Manager cancels an assigned job").
// Reverts the linked ServiceRequest to Approved so it can be reassigned.
export function cancelWorkOrder(id: number, reason: string, rowVersion: string): Promise<WorkOrder> {
  return apiFetch<WorkOrder>(`/work-orders/${id}/cancel`, {
    method: 'POST',
    body: JSON.stringify({ reason, rowVersion })
  })
}
