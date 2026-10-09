import { apiFetch } from './client'
import type { PagedResult, Part, PartFilters, PartInput, PartUpdateInput } from '../types'

// One page of the catalogue, searched and paged on the server (the API's default page size is 50, at most 200).
export function getParts(filters: PartFilters = {}): Promise<PagedResult<Part>> {
  const params = new URLSearchParams()
  if (filters.search?.trim()) params.set('search', filters.search.trim())
  if (filters.includeInactive) params.set('includeInactive', 'true')
  if (filters.page && filters.page > 1) params.set('page', String(filters.page))
  if (filters.pageSize) params.set('pageSize', String(filters.pageSize))

  const query = params.toString()
  return apiFetch<PagedResult<Part>>(`/parts${query ? `?${query}` : ''}`)
}

export function getPart(id: number): Promise<Part> {
  return apiFetch<Part>(`/parts/${id}`)
}

// AC-09: active parts at or below their minimum stock level, most urgent first.
export function getLowStockParts(): Promise<Part[]> {
  return apiFetch<Part[]>('/parts/low-stock')
}

export function createPart(input: PartInput): Promise<Part> {
  return apiFetch<Part>('/parts', {
    method: 'POST',
    body: JSON.stringify(input)
  })
}

export function updatePart(id: number, input: PartUpdateInput): Promise<Part> {
  return apiFetch<Part>(`/parts/${id}`, {
    method: 'PUT',
    body: JSON.stringify(input)
  })
}
