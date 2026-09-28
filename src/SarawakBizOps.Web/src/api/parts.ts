import { apiFetch } from './client'
import type { Part, PartFilters, PartInput, PartUpdateInput } from '../types'

export function getParts(token: string, filters: PartFilters = {}): Promise<Part[]> {
  const params = new URLSearchParams()
  if (filters.search?.trim()) params.set('search', filters.search.trim())
  if (filters.includeInactive) params.set('includeInactive', 'true')

  const query = params.toString()
  return apiFetch<Part[]>(`/parts${query ? `?${query}` : ''}`, { token })
}

// AC-09: active parts at or below their minimum stock level, most urgent first.
export function getLowStockParts(token: string): Promise<Part[]> {
  return apiFetch<Part[]>('/parts/low-stock', { token })
}

export function createPart(token: string, input: PartInput): Promise<Part> {
  return apiFetch<Part>('/parts', {
    method: 'POST',
    token,
    body: JSON.stringify(input)
  })
}

export function updatePart(token: string, id: number, input: PartUpdateInput): Promise<Part> {
  return apiFetch<Part>(`/parts/${id}`, {
    method: 'PUT',
    token,
    body: JSON.stringify(input)
  })
}
