import { useEffect, useState } from 'react'
import { Link, useSearchParams } from 'react-router-dom'
import { useAuth } from '../auth/AuthContext'
import { OFFICE_ROLES } from '../auth/roles'
import { getParts } from '../api/parts'
import { getTransactions } from '../api/inventory'
import { ApiError } from '../api/client'
import { TRANSACTION_TYPES, type InventoryTransaction, type Part, type TransactionFilters } from '../types'
import { EmptyState } from '../components/EmptyState'
import { formatDateTime, formatQuantity } from '../utils/format'

const TYPE_TONE: Record<string, string> = {
  Receive: 'tone-green',
  Issue: 'tone-steel',
  Return: 'tone-amber',
  Adjustment: 'tone-grey'
}

// The ledger is read-only by design (BR-09): this page lists and filters, it never edits.
export function InventoryHistoryPage() {
  const { auth, hasRole } = useAuth()
  const canOpenWorkOrders = hasRole(...OFFICE_ROLES)
  const [searchParams] = useSearchParams()

  const initialPartId = Number(searchParams.get('partId')) || ''
  const [filters, setFilters] = useState<TransactionFilters>({
    partId: initialPartId, type: '', workOrderId: '', fromDate: '', toDate: ''
  })
  const [parts, setParts] = useState<Part[]>([])
  const [rows, setRows] = useState<InventoryTransaction[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    if (!auth) return
    // Inactive parts still have history, so the picker offers them too.
    getParts(auth.token, { includeInactive: true })
      .then(setParts)
      .catch(() => setParts([]))
  }, [auth])

  const datesInverted = !!filters.fromDate && !!filters.toDate && filters.fromDate > filters.toDate

  useEffect(() => {
    if (!auth || datesInverted) return
    let cancelled = false

    setLoading(true)
    setError(null)
    getTransactions(auth.token, filters)
      .then(data => {
        if (!cancelled) setRows(data)
      })
      .catch(err => {
        if (!cancelled) setError(err instanceof ApiError ? err.message : 'Could not load the transaction history.')
      })
      .finally(() => {
        if (!cancelled) setLoading(false)
      })

    return () => {
      cancelled = true
    }
  }, [auth, filters, datesInverted])

  const filtered = Object.values(filters).some(v => v !== '' && v !== undefined)

  return (
    <div>
      <div className="breadcrumb">
        <Link to="/inventory">Inventory</Link> / Transaction history
      </div>
      <div className="page-header">
        <div>
          <h1 className="page-title">Transaction history</h1>
          <p className="page-subtitle">
            Every stock movement, newest first. Entries are permanent: a mistake is corrected with a new adjustment.
          </p>
        </div>
      </div>

      <div className="toolbar">
        <label className="field field-inline">
          <span className="field-label">Part</span>
          <select
            value={filters.partId ?? ''}
            onChange={e => setFilters({ ...filters, partId: e.target.value ? Number(e.target.value) : '' })}
          >
            <option value="">All parts</option>
            {parts.map(p => (
              <option key={p.id} value={p.id}>{p.partNumber} · {p.name}</option>
            ))}
          </select>
        </label>

        <label className="field field-inline">
          <span className="field-label">Type</span>
          <select
            value={filters.type ?? ''}
            onChange={e => setFilters({ ...filters, type: e.target.value as TransactionFilters['type'] })}
          >
            <option value="">All types</option>
            {TRANSACTION_TYPES.map(t => (
              <option key={t} value={t}>{t}</option>
            ))}
          </select>
        </label>

        <label className="field field-inline">
          <span className="field-label">Work order #</span>
          <input
            type="number"
            min="1"
            value={filters.workOrderId ?? ''}
            onChange={e => setFilters({ ...filters, workOrderId: e.target.value ? Number(e.target.value) : '' })}
          />
        </label>

        <label className="field field-inline">
          <span className="field-label">From (MYT)</span>
          <input
            type="date"
            value={filters.fromDate ?? ''}
            onChange={e => setFilters({ ...filters, fromDate: e.target.value })}
          />
        </label>

        <label className="field field-inline">
          <span className="field-label">To (MYT)</span>
          <input
            type="date"
            value={filters.toDate ?? ''}
            onChange={e => setFilters({ ...filters, toDate: e.target.value })}
          />
        </label>
      </div>

      {datesInverted && <p className="form-error">The “From” date is after the “To” date.</p>}
      {error && <p className="form-error">{error}</p>}

      {datesInverted ? null : loading ? (
        <p className="page-subtitle">Loading transactions…</p>
      ) : rows.length === 0 ? (
        <EmptyState
          title={filtered ? 'No transactions match these filters' : 'No stock movements yet'}
          description={filtered
            ? 'Try a wider date range or a different part or type.'
            : 'Receiving a delivery on the Inventory page creates the first entry.'}
        />
      ) : (
        <div className="table-scroll">
          <table className="data-table">
            <thead>
              <tr>
                <th>When (MYT)</th>
                <th>Part</th>
                <th>Type</th>
                <th className="num">Quantity</th>
                <th>Work order</th>
                <th>By</th>
                <th>Notes</th>
              </tr>
            </thead>
            <tbody>
              {rows.map(t => (
                <tr key={t.id}>
                  <td className="mono">{formatDateTime(t.createdAt)}</td>
                  <td><span className="mono">{t.partNumber}</span> · {t.partName}</td>
                  <td><span className={`status-badge ${TYPE_TONE[t.transactionType] ?? 'tone-grey'}`}>{t.transactionType}</span></td>
                  <td className="num mono">
                    {t.quantity > 0 ? '+' : ''}{formatQuantity(t.quantity)} {t.unit}
                  </td>
                  <td>
                    {t.workOrderId == null
                      ? '—'
                      : canOpenWorkOrders
                        ? <Link to={`/work-orders/${t.workOrderId}`}>#{t.workOrderId}</Link>
                        : `#${t.workOrderId}`}
                  </td>
                  <td>{t.createdByName}</td>
                  <td className="prose">{t.notes || '—'}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </div>
  )
}
