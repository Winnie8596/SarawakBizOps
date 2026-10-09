import { apiFetch } from './client'
import type { ManagerInsight, ManagerInsightTopic, TechnicianAssistance } from '../types'

// ---- Phase 13: the AI technician assistant ----

// The job's own Technician or a Manager. Read-only on the server: it never changes the job. A provider failure or
// timeout is a 503 and asking too often a 429, both with a ProblemDetails message the panel shows as is.
export function getTechnicianAssistance(workOrderId: number, observations?: string): Promise<TechnicianAssistance> {
  return apiFetch<TechnicianAssistance>('/ai/technician-assistant', {
    method: 'POST',
    body: JSON.stringify({ workOrderId, observations: observations || undefined })
  })
}

// ---- Phase 14: AI Manager Insights (Manager only) ----

// What can be asked, with an example question each. No model call, so it does not count against the AI rate limit.
export function getManagerInsightTopics(): Promise<ManagerInsightTopic[]> {
  return apiFetch<ManagerInsightTopic[]>('/ai/manager-insights/topics')
}

// Always 200 — an answer or a refusal (see ManagerInsight); asking too often is a 429.
export function askManagerInsights(question: string): Promise<ManagerInsight> {
  return apiFetch<ManagerInsight>('/ai/manager-insights', {
    method: 'POST',
    body: JSON.stringify({ question })
  })
}
