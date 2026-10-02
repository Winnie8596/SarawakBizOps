import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { useAuth } from '../auth/AuthContext'
import { getWorkOrders } from '../api/workOrders'
import { ApiError } from '../api/client'
import { WORK_ORDER_STATUSES, type WorkOrder, type WorkOrderFilters } from '../types'
import { EmptyState } from '../components/EmptyState'
import { PriorityBadge } from '../components/PriorityBadge'
import { StatusBadge } from '../components/StatusBadge'
import { formatDateTime } from '../utils/format'

// New work orders don't exist yet (assignment creates one straight into Assigned), so it is left out
// of the filter list even though the API enum still has it for the lifecycle to come.
const FILTERABLE_STATUSES = WORK_ORDER_STATUSES.filter(s => s !== 'New')

export function WorkOrdersPage() {
  const { auth } = useAuth()
  const [filters, setFilters] = useState<WorkOrderFilters>({ status: '' })
  const [workOrders, setWorkOrders] = useState<WorkOrder[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    if (!auth) return
    let cancelled = false

    setLoading(true)
    setError(null)
    getWorkOrders(auth.token, filters)
      .then(data => {
        if (!cancelled) setWorkOrders(data)
      })
      .catch(err => {
        if (!cancelled) setError(err instanceof ApiError ? err.message : 'Could not load work orders.')
      })
      .finally(() => {
        if (!cancelled) setLoading(false)
      })

    return () => {
      cancelled = true
    }
  }, [auth, filters])

  return (
    <div>
      <div className="page-header">
        <div>
          <h1 className="page-title">Work orders</h1>
          <p className="page-subtitle">Jobs assigned to a technician, from the first tap to Manager approval.</p>
        </div>
      </div>

      <div className="toolbar">
        <label className="field field-inline">
          <span className="field-label">Status</span>
          <select
            value={filters.status ?? ''}
            onChange={e => setFilters({ status: e.target.value as WorkOrderFilters['status'] })}
          >
            <option value="">All statuses</option>
            {FILTERABLE_STATUSES.map(s => (
              <option key={s} value={s}>{s}</option>
            ))}
          </select>
        </label>
      </div>

      {error && <p className="form-error">{error}</p>}

      {loading ? (
        <p className="page-subtitle">Loading work orders…</p>
      ) : workOrders.length === 0 ? (
        <EmptyState
          title={filters.status ? 'No work orders match this filter' : 'No work orders yet'}
          description={filters.status
            ? 'Try a different status.'
            : 'Assign an approved service request to a technician to start one.'}
        />
      ) : (
        <div className="table-scroll">
          <table className="data-table">
            <thead>
              <tr>
                <th>#</th>
                <th>Customer</th>
                <th>Equipment</th>
                <th>Technician</th>
                <th>Priority</th>
                <th>Status</th>
                <th>Assigned (MYT)</th>
              </tr>
            </thead>
            <tbody>
              {workOrders.map(w => (
                <tr key={w.id}>
                  <td><Link to={`/work-orders/${w.id}`}>#{w.id}</Link></td>
                  <td>{w.customerName}</td>
                  <td><span className="mono">{w.equipmentSerialNumber}</span> · {w.equipmentType}</td>
                  <td>{w.technicianName}</td>
                  <td><PriorityBadge priority={w.priority} /></td>
                  <td><StatusBadge status={w.status} /></td>
                  <td className="mono">{w.assignedAt ? formatDateTime(w.assignedAt) : '—'}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </div>
  )
}
