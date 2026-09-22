import { useEffect, useState, type FormEvent } from 'react'
import { Link, useParams } from 'react-router-dom'
import { useAuth } from '../auth/AuthContext'
import { approveWorkOrder, cancelWorkOrder, getWorkOrder } from '../api/workOrders'
import { ApiError } from '../api/client'
import type { WorkOrder } from '../types'
import { PriorityBadge } from '../components/PriorityBadge'
import { StatusBadge } from '../components/StatusBadge'
import { ConflictBanner } from '../components/ConflictBanner'
import { StatusTimeline } from '../components/StatusTimeline'
import { useGuardedAction } from '../hooks/useGuardedAction'

const CANCELLABLE_STATUSES = ['New', 'Assigned', 'InProgress']

export function WorkOrderDetailPage() {
  const { auth, hasRole } = useAuth()
  const { id } = useParams()
  const workOrderId = Number(id)
  const canApprove = hasRole('Manager')
  const canCancelJob = hasRole('Manager')

  const [workOrder, setWorkOrder] = useState<WorkOrder | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  const [cancelling, setCancelling] = useState(false)
  const [cancelReason, setCancelReason] = useState('')

  const reload = () => getWorkOrder(auth!.token, workOrderId)
  const { busy, error: actionError, run } = useGuardedAction(reload)

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

  function handleApprove() {
    if (!auth || !workOrder) return
    void run(
      () => approveWorkOrder(auth.token, workOrderId, workOrder.rowVersion),
      updated => setWorkOrder(updated),
      'Could not approve this work order.'
    )
  }

  function handleCancel(event: FormEvent) {
    event.preventDefault()
    if (!auth || !workOrder) return
    void run(
      () => cancelWorkOrder(auth.token, workOrderId, cancelReason.trim(), workOrder.rowVersion),
      updated => {
        setWorkOrder(updated)
        setCancelling(false)
        setCancelReason('')
      },
      'Could not cancel this work order.'
    )
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

  const locked = workOrder.status === 'Approved' || workOrder.status === 'Cancelled'
  const canCancel = canCancelJob && CANCELLABLE_STATUSES.includes(workOrder.status)

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
      </dl>

      {locked && workOrder.status === 'Approved' && (
        <p className="form-hint">This job is Approved and locked — it can no longer be edited.</p>
      )}

      {(workOrder.diagnosis || workOrder.workPerformed) && (
        <dl className="detail-list panel">
          <div><dt>Diagnosis</dt><dd className="prose">{workOrder.diagnosis}</dd></div>
          <div><dt>Work performed</dt><dd className="prose">{workOrder.workPerformed}</dd></div>
          {workOrder.notes && <div><dt>Notes</dt><dd className="prose">{workOrder.notes}</dd></div>}
        </dl>
      )}

      <StatusTimeline
        entries={[
          { label: 'Assigned', at: workOrder.assignedAt },
          { label: 'Started', at: workOrder.startedAt },
          { label: 'Completed', at: workOrder.completedAt },
          { label: 'Approved', at: workOrder.approvedAt },
          {
            label: 'Cancelled',
            at: workOrder.cancelledAt,
            by: workOrder.cancelledByName,
            detail: workOrder.cancellationReason
          }
        ]}
      />

      <ConflictBanner message={actionError} />

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

      {canCancel && (
        <div className="panel">
          <h2 className="panel-title">Cancel this job</h2>

          {cancelling ? (
            <form onSubmit={handleCancel}>
              <div className="field">
                <label className="field-label" htmlFor="wo-cancel-reason">Reason for cancelling</label>
                <textarea
                  id="wo-cancel-reason"
                  value={cancelReason}
                  onChange={e => setCancelReason(e.target.value)}
                  maxLength={500}
                  rows={3}
                  required
                  autoFocus
                />
                <span className="form-hint">
                  The service request returns to Approved so it can be reassigned.
                </span>
              </div>
              <div className="panel-actions">
                <button type="button" className="btn btn-ghost" onClick={() => setCancelling(false)} disabled={busy}>
                  Back
                </button>
                <button type="submit" className="btn btn-primary" disabled={busy || cancelReason.trim() === ''}>
                  {busy ? 'Cancelling…' : 'Confirm cancellation'}
                </button>
              </div>
            </form>
          ) : (
            <div className="panel-actions">
              <button className="btn btn-ghost" onClick={() => setCancelling(true)} disabled={busy}>
                Cancel job…
              </button>
            </div>
          )}
        </div>
      )}
    </div>
  )
}
