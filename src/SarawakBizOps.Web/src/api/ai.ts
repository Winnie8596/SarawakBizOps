import { apiFetch } from './client'
import type { TechnicianAssistance } from '../types'

// ---- Phase 13: the AI technician assistant ----

// The job's own Technician or a Manager. Read-only on the server: it never changes the job. A provider failure or
// timeout is a 503 and asking too often a 429, both with a ProblemDetails message the panel shows as is.
export function getTechnicianAssistance(
  token: string,
  workOrderId: number,
  observations?: string
): Promise<TechnicianAssistance> {
  return apiFetch<TechnicianAssistance>('/ai/technician-assistant', {
    method: 'POST',
    token,
    body: JSON.stringify({ workOrderId, observations: observations || undefined })
  })
}
