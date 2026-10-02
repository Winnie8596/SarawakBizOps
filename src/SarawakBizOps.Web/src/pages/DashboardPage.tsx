import { useCallback, useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { useAuth } from '../auth/AuthContext'
import { getDashboardSummary } from '../api/dashboard'
import { ApiError } from '../api/client'
import type {
  AnalyticsPeriod,
  DashboardView,
  DurationStat,
  LowStockSummary,
  ManagerDashboard,
  PartUsage,
  ServiceQueueDashboard,
  WarehouseDashboard
} from '../types'
import { BarTable, MonthColumns } from '../components/Charts'
import { PriorityBadge } from '../components/PriorityBadge'
import { StatusBadge, formatStatus } from '../components/StatusBadge'
import { formatDateTime, formatHours, formatMoney, formatPeriod, formatQuantity } from '../utils/format'

// Phase 11: the Manager (and Admin) get the operational dashboard (PRD §6.9, SDD §14). Phase 12 (PRD §5): every
// other office role gets its own view from the same endpoint — the server picks it from the caller's role and
// tags the payload with `view`, so the page never decides what a role may see. A Technician lands on My Jobs.
export function DashboardPage() {
  const { auth } = useAuth()
  const [dashboard, setDashboard] = useState<DashboardView | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  const load = useCallback(async (isCancelled: () => boolean = () => false) => {
    if (!auth) return
    setLoading(true)
    setError(null)
    try {
      const data = await getDashboardSummary(auth.token)
      if (!isCancelled()) setDashboard(data)
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
    <div>
      <h1 className="page-title">Welcome back, {auth?.fullName.split(' ')[0]}</h1>

      <div className="page-header">
        <p className="page-subtitle">
          {dashboard ? INTRO[dashboard.view] : 'Your dashboard, computed by the server.'} Times are Malaysia time (MYT).
          {dashboard && <> Updated {formatDateTime(dashboard.generatedAt)}.</>}
        </p>
        <button className="btn btn-ghost" onClick={() => load()} disabled={loading}>
          {loading && dashboard ? 'Refreshing…' : 'Refresh'}
        </button>
      </div>

      {error && (
        <p className="form-error" role="alert">
          {error}
        </p>
      )}

      {!dashboard ? (
        loading && <p className="page-subtitle">Loading the dashboard…</p>
      ) : dashboard.view === 'Manager' ? (
        <ManagerView summary={dashboard} />
      ) : dashboard.view === 'Warehouse' ? (
        <WarehouseView summary={dashboard} />
      ) : (
        <ServiceQueueView summary={dashboard} />
      )}
    </div>
  )
}

const INTRO: Record<DashboardView['view'], string> = {
  Manager: 'Today’s workload and the last six months, computed by the server.',
  Warehouse: 'Stock, the jobs waiting for parts and what the jobs use.',
  ServiceQueue: 'The service request queue: what is waiting, and for how long.'
}

// ---------- Manager and Admin ----------

function ManagerView({ summary }: { summary: ManagerDashboard }) {
  const period = periodLabel(summary.period)

  return (
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

        <section className="panel" aria-labelledby="chart-turnaround">
          <h2 className="panel-title" id="chart-turnaround">Turnaround, {period}</h2>
          <dl className="metric-list">
            <Metric
              label="Request raised → technician assigned"
              stat={summary.averageTimeToAssignment}
              noun="assignment"
            />
            <Metric
              label="Job started → completed"
              stat={summary.averageCompletionDuration}
              noun="completed job"
            />
          </dl>
          <p className="muted">Averages. Time waiting for parts counts towards a job’s duration.</p>
        </section>

        <section className="panel" aria-labelledby="chart-failures">
          <h2 className="panel-title" id="chart-failures">Repeated failures, {period}</h2>
          <RepeatedFailures summary={summary} />
        </section>

        <section className="panel" aria-labelledby="chart-parts">
          <h2 className="panel-title" id="chart-parts">Top parts by usage, {period}</h2>
          <PartsUsage parts={summary.topPartsByUsage} period={period} />
        </section>

        <section className="panel" aria-labelledby="chart-stock">
          <h2 className="panel-title" id="chart-stock">Low stock</h2>
          <LowStock lowStock={summary.lowStock} />
        </section>
      </div>
    </>
  )
}

function Metric({ label, stat, noun }: { label: string; stat: DurationStat; noun: string }) {
  return (
    <div className="metric">
      <dt>{label}</dt>
      <dd>
        <span className="metric-value">{formatHours(stat.averageHours)}</span>
        <span className="muted">
          {stat.count === 0 ? 'nothing to measure yet' : `over ${stat.count} ${stat.count === 1 ? noun : `${noun}s`}`}
        </span>
      </dd>
    </div>
  )
}

function RepeatedFailures({ summary }: { summary: ManagerDashboard }) {
  const { minimumRequests, equipmentCount, items } = summary.repeatedFailures
  if (equipmentCount === 0) {
    return <p className="chart-empty">No equipment has had {minimumRequests} or more fault reports in this period.</p>
  }

  return (
    <>
      <p className="page-subtitle">
        {equipmentCount} {equipmentCount === 1 ? 'piece' : 'pieces'} of equipment with {minimumRequests} or more
        fault reports (cancelled and rejected requests not counted).
      </p>
      <div className="table-scroll">
        <table className="data-table">
          <caption className="visually-hidden">Equipment with repeated fault reports</caption>
          <thead>
            <tr>
              <th>Equipment</th>
              <th className="num">Reports</th>
              <th>Last reported</th>
            </tr>
          </thead>
          <tbody>
            {items.map(item => (
              <tr key={item.equipmentId}>
                <td>
                  <Link to={`/equipment/${item.equipmentId}`} className="mono">{item.serialNumber}</Link>
                  <br />
                  <span className="muted">{item.equipmentType} · {item.customerName}</span>
                </td>
                <td className="num">{item.requestCount}</td>
                <td>{formatDateTime(item.lastReportedAt)}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </>
  )
}

// ---------- WarehouseStaff ----------

function WarehouseView({ summary }: { summary: WarehouseDashboard }) {
  const { kpis, jobsWaitingForParts: waiting } = summary

  return (
    <>
      <div className="stat-grid stat-grid-wrap">
        <Tile to="/inventory" value={kpis.activeParts} label="Active parts" />
        <Tile to="/inventory" value={kpis.lowStockParts} label="Low on stock" warn={kpis.lowStockParts > 0} />
        <Tile to="/inventory" value={kpis.outOfStockParts} label="Out of stock" warn={kpis.outOfStockParts > 0} />
        <Tile to="/inventory" value={kpis.jobsWaitingForParts} label="Jobs waiting for parts"
          warn={kpis.jobsWaitingForParts > 0} />
      </div>

      <div className="dashboard-grid">
        <section className="panel" aria-labelledby="chart-waiting">
          <h2 className="panel-title" id="chart-waiting">Jobs waiting for parts</h2>
          {waiting.count === 0 ? (
            <p className="chart-empty">No job is waiting for parts.</p>
          ) : (
            <>
              <p className="page-subtitle">
                {waiting.count} {waiting.count === 1 ? 'job is' : 'jobs are'} paused for a part
                {waiting.readyToResume > 0 && <>; {waiting.readyToResume} can resume now</>}. Longest wait first.
              </p>
              <div className="table-scroll">
                <table className="data-table">
                  <caption className="visually-hidden">Jobs paused waiting for a part</caption>
                  <thead>
                    <tr>
                      <th>Job</th>
                      <th>Part</th>
                      <th className="num">Needed</th>
                      <th className="num">On hand</th>
                    </tr>
                  </thead>
                  <tbody>
                    {waiting.items.map(job => (
                      <tr key={job.workOrderId} className={job.inStock ? undefined : 'row-warn'}>
                        <td>
                          #{job.workOrderId}
                          {job.waitingSince && (
                            <>
                              <br />
                              <span className="muted">since {formatDateTime(job.waitingSince)}</span>
                            </>
                          )}
                        </td>
                        <td>
                          <span className="mono">{job.partNumber}</span>
                          <br />
                          <span className="muted">{job.name}</span>
                        </td>
                        <td className="num">{formatQuantity(job.quantityNeeded)} {job.unit}</td>
                        <td className="num">
                          {formatQuantity(job.quantityOnHand)} {job.unit}
                          <br />
                          <span className="muted">{job.inStock ? 'In stock' : 'Short'}</span>
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            </>
          )}
        </section>

        <section className="panel" aria-labelledby="chart-stock">
          <h2 className="panel-title" id="chart-stock">Low stock</h2>
          <LowStock lowStock={summary.lowStock} />
        </section>

        <section className="panel" aria-labelledby="chart-parts">
          <h2 className="panel-title" id="chart-parts">Top parts by usage, {periodLabel(summary.period)}</h2>
          <PartsUsage parts={summary.topPartsByUsage} period={periodLabel(summary.period)} />
        </section>
      </div>
    </>
  )
}

// ---------- ServiceStaff ----------

function ServiceQueueView({ summary }: { summary: ServiceQueueDashboard }) {
  const { kpis, queue } = summary

  return (
    <>
      <div className="stat-grid stat-grid-wrap">
        <Tile to="/service-requests" value={kpis.awaitingReview} label="Awaiting review" />
        <Tile to="/service-requests" value={kpis.awaitingAssignment} label="Awaiting assignment" />
        <Tile to="/service-requests" value={kpis.rejectedRecently} label={`Rejected in the last ${queue.recentDays} days`}
          warn={kpis.rejectedRecently > 0} />
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

        <section className="panel" aria-labelledby="chart-oldest">
          <h2 className="panel-title" id="chart-oldest">Waiting longest</h2>
          {queue.oldestOpen.length === 0 ? (
            <p className="chart-empty">Nothing is waiting for review or assignment.</p>
          ) : (
            <div className="table-scroll">
              <table className="data-table">
                <caption className="visually-hidden">Open requests, the longest-waiting first</caption>
                <thead>
                  <tr>
                    <th>Request</th>
                    <th>Priority</th>
                    <th>Status</th>
                    <th>Raised</th>
                  </tr>
                </thead>
                <tbody>
                  {queue.oldestOpen.map(r => (
                    <tr key={r.id}>
                      <td>
                        <Link to={`/service-requests/${r.id}`}>#{r.id}</Link> <span className="mono">{r.equipmentSerialNumber}</span>
                        <br />
                        <span className="muted">{r.equipmentType} · {r.customerName}</span>
                      </td>
                      <td><PriorityBadge priority={r.priority} /></td>
                      <td><StatusBadge status={r.status} /></td>
                      <td>{formatDateTime(r.createdAt)}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </section>

        <section className="panel" aria-labelledby="chart-rejected">
          <h2 className="panel-title" id="chart-rejected">Recently rejected</h2>
          {queue.recentlyRejected.length === 0 ? (
            <p className="chart-empty">No requests rejected in the last {queue.recentDays} days.</p>
          ) : (
            <>
              <p className="page-subtitle">Let the customer know why, or raise a new request if things have changed.</p>
              <ul className="plain-list">
                {queue.recentlyRejected.map(r => (
                  <li key={r.id}>
                    <Link to={`/service-requests/${r.id}`}>#{r.id}</Link> <span className="mono">{r.equipmentSerialNumber}</span>
                    {' · '}{r.customerName}
                    <br />
                    <span className="muted">Rejected {formatDateTime(r.rejectedAt)}: {r.rejectionReason}</span>
                  </li>
                ))}
              </ul>
            </>
          )}
        </section>
      </div>
    </>
  )
}

// ---------- Shared pieces ----------

function periodLabel(period: AnalyticsPeriod): string {
  return formatPeriod(period.fromMonth, period.toMonth)
}

function Tile({ to, value, label, warn = false }: { to: string; value: number; label: string; warn?: boolean }) {
  return (
    <Link to={to} className={`stat-card stat-card-link${warn ? ' stat-card-warn' : ''}`}>
      <span className="stat-value">{value}</span>
      <span className="stat-label">{label}</span>
    </Link>
  )
}

function PartsUsage({ parts, period }: { parts: PartUsage[]; period: string }) {
  if (parts.length === 0) return <p className="chart-empty">No parts were used on jobs in {period}.</p>

  return (
    <div className="table-scroll">
      <table className="data-table">
        <caption className="visually-hidden">Parts used on jobs, costliest first</caption>
        <thead>
          <tr>
            <th>Part</th>
            <th className="num">Used</th>
            <th className="num">Jobs</th>
            <th className="num">Cost</th>
          </tr>
        </thead>
        <tbody>
          {parts.map(part => (
            <tr key={part.partId}>
              <td>
                <span className="mono">{part.partNumber}</span>
                <br />
                <span className="muted">{part.name}</span>
              </td>
              <td className="num">{formatQuantity(part.quantity)} {part.unit}</td>
              <td className="num">{part.jobCount}</td>
              <td className="num">{formatMoney(part.cost)}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  )
}

function LowStock({ lowStock }: { lowStock: LowStockSummary }) {
  const { count, outOfStock, items } = lowStock
  if (count === 0) return <p className="chart-empty">Every active part is above its minimum stock level.</p>

  return (
    <>
      <p className="page-subtitle">
        {count} active {count === 1 ? 'part is' : 'parts are'} at or below the minimum
        {outOfStock > 0 && <>; {outOfStock} out of stock</>}.
      </p>
      <div className="table-scroll">
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
      </div>
      <p className="panel-actions">
        <Link to="/inventory">{count > items.length ? `View all ${count} in Inventory` : 'Open Inventory'}</Link>
      </p>
    </>
  )
}
