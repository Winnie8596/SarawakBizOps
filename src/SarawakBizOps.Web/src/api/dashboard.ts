import { apiFetch } from './client'
import type { DashboardSummary } from '../types'

// ---- Phase 11: the Manager dashboard ----

// Manager and Admin. Every number is computed by the API's analytics operations; the page only draws them.
export function getDashboardSummary(token: string): Promise<DashboardSummary> {
  return apiFetch<DashboardSummary>('/dashboard/summary', { token })
}
