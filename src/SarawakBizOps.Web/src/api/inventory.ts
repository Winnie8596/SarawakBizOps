import { apiFetch } from './client'
import type { InventoryTransaction, StockMovementInput, TransactionFilters } from '../types'
import { mytDayStartUtc } from '../utils/format'

export function getTransactions(token: string, filters: TransactionFilters = {}): Promise<InventoryTransaction[]> {
  const params = new URLSearchParams()
  if (filters.partId) params.set('partId', String(filters.partId))
  if (filters.type) params.set('type', filters.type)
  if (filters.workOrderId) params.set('workOrderId', String(filters.workOrderId))
  // The API takes a UTC range with an exclusive end; the UI picks whole MYT days, inclusive.
  if (filters.fromDate) params.set('from', mytDayStartUtc(filters.fromDate))
  if (filters.toDate) params.set('to', mytDayStartUtc(filters.toDate, 1))

  const query = params.toString()
  return apiFetch<InventoryTransaction[]>(`/inventory-transactions${query ? `?${query}` : ''}`, { token })
}

// Receive or Adjustment only. The ledger has no edit or delete: a mistake is fixed with an offsetting Adjustment.
export function recordStockMovement(token: string, input: StockMovementInput): Promise<InventoryTransaction> {
  return apiFetch<InventoryTransaction>('/inventory-transactions', {
    method: 'POST',
    token,
    body: JSON.stringify(input)
  })
}
