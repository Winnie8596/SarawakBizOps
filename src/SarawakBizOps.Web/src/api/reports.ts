import { apiFetch, apiFetchBlob } from './client'
import type { ServiceReport, WorkOrder } from '../types'

// ---- Phase 10: the service report PDF ----

// Every generated report, newest first (office roles).
export function getReports(): Promise<ServiceReport[]> {
  return apiFetch<ServiceReport[]>('/reports')
}

// Manager only. Renders the report from the completed job and returns the job with its report; regenerating a
// Completed job keeps the number and bumps the revision, and an Approved job's report is locked (409).
export function generateReport(workOrderId: number): Promise<WorkOrder> {
  return apiFetch<WorkOrder>(`/work-orders/${workOrderId}/generate-report`, { method: 'POST' })
}

// Like the signature and photos, the PDF has no public URL: it is only ever read through this authorized endpoint.
export function getReportPdf(workOrderId: number): Promise<Blob> {
  return apiFetchBlob(`/work-orders/${workOrderId}/report`)
}
