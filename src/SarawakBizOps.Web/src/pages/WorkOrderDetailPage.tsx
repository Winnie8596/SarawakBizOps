import { useEffect, useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import { useAuth } from '../auth/AuthContext'
import { approveWorkOrder, getWorkOrder } from '../api/workOrders'
import { ApiError } from '../api/client'
import type { WorkOrder } from '../types'
import { PriorityBadge } from '../components/PriorityBadge'
import { StatusBadge } from '../components/StatusBadge'
import { formatDateTime } from '../utils/format'

export function WorkOrderDetailPage() {
  const { auth, hasRole } = useAuth()
  const { id } = useParams()
  const workOrderId = Number(id)
  const canApprove = hasRole('Manager')

  const [workOrder, setWorkOrder] = useState<WorkOrder | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const [actionError, setActionError] = useState<string | null>(null)

  useEffect(() => {
    if (!auth) return
    let cancelled = false

    setLoading(true)
    setError(null)
    getWorkOrder(auth.token, workOrderId)
      .then(data => {
        if (!cancelled) setWorkOrder(data)
      })
      .catch(err => {
        if (!cancelled) setError(err instanceof ApiError ? err.message : 'Could not load this work order.')
      })
      .finally(() => {
        if (!cancelled) setLoading(false)
      })

    return () => {
      cancelled = true
    }
  }, [auth, workOrderId])

  // A 409 means someone else already approved it (or it moved on); reload so the page matches reality.
  async function handleApprove() {
    if (!auth) return
    setBusy(true)
    setActionError(null)
    try {
      setWorkOrder(await approveWorkOrder(auth.token, workOrderId))
    } catch (err) {
      setActionError(err instanceof ApiError ? err.message : 'Could not approve this work order.')
      if (err instanceof ApiError && err.status === 409) {
        try {
          setWorkOrder(await getWorkOrder(auth.token, workOrderId))
        } catch {
          // The message above already explains the conflict; the page keeps its previous data.
        }
      }
    } finally {
      setBusy(false)
    }
  }

  if (loading) return <p className="page-subtitle">Loading work order…</p>
  if (error || !workOrder) {
    return (
      <div>
        <p className="form-error">{error ?? 'Work order not found.'}</p>
        <Link to="/work-orders">← Back to work orders</Link>
      </div>
    )
  }

  return (
    <div>
      <p className="breadcrumb"><Link to="/work-orders">← Work orders</Link></p>
      <h1 className="page-title">
        Work order #{workOrder.id} <StatusBadge status={workOrder.status} />
      </h1>

      <dl className="detail-list panel">
        <div>
          <dt>Customer</dt>
          <dd><Link to={`/customers/${workOrder.customerId}`}>{workOrder.customerName}</Link></dd>
        </div>
        <div>
          <dt>Equipment</dt>
          <dd>
            <Link to={`/equipment/${workOrder.equipmentId}`}>
              <span className="mono">{workOrder.equipmentSerialNumber}</span>
            </Link>{' '}
            · {workOrder.equipmentType}
          </dd>
        </div>
        <div><dt>Priority</dt><dd><PriorityBadge priority={workOrder.priority} /></dd></div>
        <div><dt>Problem</dt><dd className="prose">{workOrder.problemDescription}</dd></div>
        <div><dt>Technician</dt><dd>{workOrder.technicianName}</dd></div>
        <div>
          <dt>Service request</dt>
          <dd><Link to={`/service-requests/${workOrder.serviceRequestId}`}>#{workOrder.serviceRequestId}</Link></dd>
        </div>
        {workOrder.assignedAt && <div><dt>Assigned</dt><dd>{formatDateTime(workOrder.assignedAt)}</dd></div>}
        {workOrder.startedAt && <div><dt>Started</dt><dd>{formatDateTime(workOrder.startedAt)}</dd></div>}
        {workOrder.completedAt && <div><dt>Completed</dt><dd>{formatDateTime(workOrder.completedAt)}</dd></div>}
        {workOrder.approvedAt && <div><dt>Approved</dt><dd>{formatDateTime(workOrder.approvedAt)}</dd></div>}
      </dl>

      {(workOrder.diagnosis || workOrder.workPerformed) && (
        <dl className="detail-list panel">
          <div><dt>Diagnosis</dt><dd className="prose">{workOrder.diagnosis}</dd></div>
          <div><dt>Work performed</dt><dd className="prose">{workOrder.workPerformed}</dd></div>
          {workOrder.notes && <div><dt>Notes</dt><dd className="prose">{workOrder.notes}</dd></div>}
        </dl>
      )}

      {actionError && <p className="form-error" role="alert">{actionError}</p>}

      {workOrder.status === 'Completed' && canApprove && (
        <div className="panel-actions">
          <button className="btn btn-primary" onClick={handleApprove} disabled={busy}>
            {busy ? 'Approving…' : 'Approve'}
          </button>
        </div>
      )}

      {workOrder.status === 'Completed' && !canApprove && (
        <p className="form-hint">Waiting for a Manager to approve this job.</p>
      )}
    </div>
  )
}
