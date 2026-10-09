import { apiFetch } from './client'
import type { DashboardView } from '../types'

// ---- Phase 11: the Manager dashboard. Phase 12: every role but the Technician gets its own view ----

// The server decides the view from the caller's role and tags the payload with it (`view`). Every number is
// computed by the API's analytics operations; the page only draws them.
export function getDashboardSummary(): Promise<DashboardView> {
  return apiFetch<DashboardView>('/dashboard/summary')
}
