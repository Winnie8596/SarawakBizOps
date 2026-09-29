import { useCallback, useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { useAuth } from '../auth/AuthContext'
import { getCustomers } from '../api/customers'
import { getEquipment } from '../api/equipment'
import { getDashboardSummary } from '../api/dashboard'
import { ApiError } from '../api/client'
import type { DashboardSummary } from '../types'
import { BarTable, MonthColumns } from '../components/Charts'
import { formatStatus } from '../components/StatusBadge'
import { formatDateTime, formatQuantity } from '../utils/format'

// Phase 11: the Manager (and Admin) get the operational dashboard (PRD §6.9, SDD §14); the other office roles
// keep the simple counts until their own dashboards arrive in Phase 12.
export function DashboardPage() {
  const { auth, hasRole } = useAuth()

  return (
    <div>
      <h1 className="page-title">Welcome back, {auth?.fullName.split(' ')[0]}</h1>
      {hasRole('Manager', 'Admin') ? <ManagerDashboard /> : <SimpleCounts />}
    </div>
  )
}

function ManagerDashboard() {
  const { auth } = useAuth()
  const [summary, setSummary] = useState<DashboardSummary | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  const load = useCallback(async (isCancelled: () => boolean = () => false) => {
    if (!auth) return
    setLoading(true)
    setError(null)
    try {
      const data = await getDashboardSummary(auth.token)
      if (!isCancelled()) setSummary(data)
    } catch (err) {
      if (!isCancelled()) setError(err instanceof ApiError ? err.message : 'Could not load the dashboard.')
    } finally {
      if (!isCancelled()) setLoading(false)
    }
  }, [auth])

  useEffect(() => {
    let cancelled = false
    load(() => cancelled)
    return () => {
      cancelled = true
    }
  }, [load])

  return (
    <>
      <div className="page-header">
        <p className="page-subtitle">
          Today’s workload and the last six months, computed by the server. Times are Malaysia time (MYT).
          {summary && <> Updated {formatDateTime(summary.generatedAt)}.</>}
        </p>
        <button className="btn btn-ghost" onClick={() => load()} disabled={loading}>
          {loading && summary ? 'Refreshing…' : 'Refresh'}
        </button>
      </div>

      {error && (
        <p className="form-error" role="alert">
          {error}
        </p>
      )}

      {!summary ? (
        loading && <p className="page-subtitle">Loading the dashboard…</p>
      ) : (
        <>
          <div className="stat-grid stat-grid-wrap">
            <Tile to="/service-requests" value={summary.kpis.openRequests} label="Open requests" />
            <Tile to="/work-orders" value={summary.kpis.activeWorkOrders} label="Active jobs" />
            <Tile to="/work-orders" value={summary.kpis.awaitingApproval} label="Awaiting your approval" />
            <Tile to="/work-orders" value={summary.kpis.completedThisMonth} label="Completed this month" />
            <Tile to="/inventory" value={summary.kpis.lowStockParts} label="Parts low on stock"
              warn={summary.kpis.lowStockParts > 0} />
          </div>

          <div className="dashboard-grid">
            <section className="panel" aria-labelledby="chart-requests">
              <h2 className="panel-title" id="chart-requests">Open requests by priority</h2>
              <BarTable
                caption="Open service requests (awaiting review or assignment) by priority"
                rows={summary.openRequestsByPriority}
                empty="No open requests: everything has been reviewed and assigned."
              />
            </section>

            <section className="panel" aria-labelledby="chart-statuses">
              <h2 className="panel-title" id="chart-statuses">Work orders by status</h2>
              <BarTable
                caption="All work orders by status"
                rows={summary.workOrdersByStatus}
                label={formatStatus}
                empty="No work orders yet."
              />
            </section>

            <section className="panel" aria-labelledby="chart-completed">
              <h2 className="panel-title" id="chart-completed">Jobs completed per month</h2>
              <MonthColumns
                label="Jobs completed per month, last six months"
                months={summary.jobsCompletedPerMonth}
                unit={count => `${count} ${count === 1 ? 'job' : 'jobs'} completed`}
                empty="No jobs completed in the last six months."
              />
            </section>

            <section className="panel" aria-labelledby="chart-stock">
              <h2 className="panel-title" id="chart-stock">Low stock</h2>
              <LowStock summary={summary} />
            </section>
          </div>
        </>
      )}
    </>
  )
}

function Tile({ to, value, label, warn = false }: { to: string; value: number; label: string; warn?: boolean }) {
  return (
    <Link to={to} className={`stat-card stat-card-link${warn ? ' stat-card-warn' : ''}`}>
      <span className="stat-value">{value}</span>
      <span className="stat-label">{label}</span>
    </Link>
  )
}

function LowStock({ summary }: { summary: DashboardSummary }) {
  const { count, outOfStock, items } = summary.lowStock
  if (count === 0) return <p className="chart-empty">Every active part is above its minimum stock level.</p>

  return (
    <>
      <p className="page-subtitle">
        {count} active {count === 1 ? 'part is' : 'parts are'} at or below the minimum
        {outOfStock > 0 && <>; {outOfStock} out of stock</>}.
      </p>
      <table className="data-table">
        <thead>
          <tr>
            <th>Part</th>
            <th className="num">On hand</th>
            <th className="num">Minimum</th>
          </tr>
        </thead>
        <tbody>
          {items.map(item => (
            <tr key={item.partId} className={item.quantityOnHand <= 0 ? 'row-warn' : undefined}>
              <td>
                <span className="mono">{item.partNumber}</span>
                <br />
                <span className="muted">{item.name}</span>
              </td>
              <td className="num">{formatQuantity(item.quantityOnHand)} {item.unit}</td>
              <td className="num">{formatQuantity(item.minimumStockLevel)} {item.unit}</td>
            </tr>
          ))}
        </tbody>
      </table>
      <p className="panel-actions">
        <Link to="/inventory">{count > items.length ? `View all ${count} in Inventory` : 'Open Inventory'}</Link>
      </p>
    </>
  )
}

function SimpleCounts() {
  const { auth } = useAuth()
  const [customerCount, setCustomerCount] = useState<number | null>(null)
  const [equipmentCount, setEquipmentCount] = useState<number | null>(null)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    if (!auth) return
    let cancelled = false

    Promise.all([getCustomers(auth.token), getEquipment(auth.token)])
      .then(([customers, equipment]) => {
        if (cancelled) return
        setCustomerCount(customers.length)
        setEquipmentCount(equipment.length)
      })
      .catch(() => {
        if (!cancelled) setError('Could not load the latest counts.')
      })

    return () => {
      cancelled = true
    }
  }, [auth])

  return (
    <>
      <p className="page-subtitle">Here's what the system tracks today.</p>

      {error && <p className="form-error">{error}</p>}

      <div className="stat-grid">
        <div className="stat-card">
          <span className="stat-value">{customerCount ?? '—'}</span>
          <span className="stat-label">Customers</span>
        </div>
        <div className="stat-card">
          <span className="stat-value">{equipmentCount ?? '—'}</span>
          <span className="stat-label">Equipment records</span>
        </div>
      </div>
    </>
  )
}
