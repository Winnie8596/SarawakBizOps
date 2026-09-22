import { apiFetch } from './client'
import type { ServiceRequest, ServiceRequestFilters, ServiceRequestInput, WorkOrder } from '../types'

export function getServiceRequests(token: string, filters: ServiceRequestFilters = {}): Promise<ServiceRequest[]> {
  const params = new URLSearchParams()
  if (filters.status) params.set('status', filters.status)
  if (filters.priority) params.set('priority', filters.priority)
  if (filters.customerId) params.set('customerId', String(filters.customerId))

  const query = params.toString()
  return apiFetch<ServiceRequest[]>(`/service-requests${query ? `?${query}` : ''}`, { token })
}

export function getServiceRequest(token: string, id: number): Promise<ServiceRequest> {
  return apiFetch<ServiceRequest>(`/service-requests/${id}`, { token })
}

export function createServiceRequest(token: string, input: ServiceRequestInput): Promise<ServiceRequest> {
  return apiFetch<ServiceRequest>('/service-requests', {
    method: 'POST',
    token,
    body: JSON.stringify(input)
  })
}

export function approveServiceRequest(token: string, id: number, rowVersion: string): Promise<ServiceRequest> {
  return apiFetch<ServiceRequest>(`/service-requests/${id}/approve`, {
    method: 'POST',
    token,
    body: JSON.stringify({ rowVersion })
  })
}

export function rejectServiceRequest(token: string, id: number, reason: string, rowVersion: string): Promise<ServiceRequest> {
  return apiFetch<ServiceRequest>(`/service-requests/${id}/reject`, {
    method: 'POST',
    token,
    body: JSON.stringify({ reason, rowVersion })
  })
}

// Decision: ServiceStaff/Admin may cancel their own New request; a Manager may cancel New or Approved.
// The service layer enforces which, since it depends on both the actor's role and the current status.
export function cancelServiceRequest(token: string, id: number, reason: string, rowVersion: string): Promise<ServiceRequest> {
  return apiFetch<ServiceRequest>(`/service-requests/${id}/cancel`, {
    method: 'POST',
    token,
    body: JSON.stringify({ reason, rowVersion })
  })
}

export function assignServiceRequest(token: string, id: number, technicianId: string, rowVersion: string): Promise<WorkOrder> {
  return apiFetch<WorkOrder>(`/service-requests/${id}/assign`, {
    method: 'POST',
    token,
    body: JSON.stringify({ technicianId, rowVersion })
  })
}
