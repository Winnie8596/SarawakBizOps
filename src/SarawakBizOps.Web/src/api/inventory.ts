import { apiFetch } from './client'
import type { CursorPage, InventoryTransaction, StockMovementInput, TransactionFilters } from '../types'

// Newest first, one page at a time: pass the previous page's nextCursor to read on.
export function getTransactions(
  filters: TransactionFilters = {},
  cursor?: string | null
): Promise<CursorPage<InventoryTransaction>> {
  const params = new URLSearchParams()
  if (filters.partId) params.set('partId', String(filters.partId))
  if (filters.type) params.set('type', filters.type)
  if (filters.workOrderId) params.set('workOrderId', String(filters.workOrderId))
  // Whole MYT days, inclusive: the server works out the UTC window, so the browser's own time zone never matters.
  if (filters.fromDate) params.set('fromDate', filters.fromDate)
  if (filters.toDate) params.set('toDate', filters.toDate)
  if (cursor) params.set('cursor', cursor)

  const query = params.toString()
  return apiFetch<CursorPage<InventoryTransaction>>(`/inventory-transactions${query ? `?${query}` : ''}`)
}

// Receive or Adjustment only. The ledger has no edit or delete: a mistake is fixed with an offsetting Adjustment.
export function recordStockMovement(input: StockMovementInput): Promise<InventoryTransaction> {
  return apiFetch<InventoryTransaction>('/inventory-transactions', {
    method: 'POST',
    body: JSON.stringify(input)
  })
}
