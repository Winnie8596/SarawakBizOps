import { useEffect, useState, type FormEvent } from 'react'
import { Link, useParams } from 'react-router-dom'
import { useAuth } from '../auth/AuthContext'
import {
  approveServiceRequest,
  assignServiceRequest,
  cancelServiceRequest,
  getServiceRequest,
  rejectServiceRequest
} from '../api/serviceRequests'
import { getTechnicians } from '../api/technicians'
import { ApiError } from '../api/client'
import type { ServiceRequest, Technician } from '../types'
import { PriorityBadge } from '../components/PriorityBadge'
import { StatusBadge } from '../components/StatusBadge'
import { ConflictBanner } from '../components/ConflictBanner'
import { StatusTimeline } from '../components/StatusTimeline'
import { useGuardedAction } from '../hooks/useGuardedAction'

export function ServiceRequestDetailPage() {
  const { auth, hasRole } = useAuth()
  const { id } = useParams()
  const requestId = Number(id)
  const canReview = hasRole('Manager')
  const canAssign = hasRole('Manager')

  const [request, setRequest] = useState<ServiceRequest | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  const [rejecting, setRejecting] = useState(false)
  const [reason, setReason] = useState('')

  const [cancelling, setCancelling] = useState(false)
  const [cancelReason, setCancelReason] = useState('')

  const [assigning, setAssigning] = useState(false)
  const [technicians, setTechnicians] = useState<Technician[]>([])
  const [technicianId, setTechnicianId] = useState('')

  const reload = () => getServiceRequest(auth!.token, requestId)
  const { busy, error: actionError, run } = useGuardedAction(reload)

  useEffect(() => {
    if (!auth) return
    let cancelled = false

    setLoading(true)
    setError(null)
    getServiceRequest(auth.token, requestId)
      .then(data => {
        if (!cancelled) setRequest(data)
      })
      .catch(err => {
        if (!cancelled) setError(err instanceof ApiError ? err.message : 'Could not load this service request.')
      })
      .finally(() => {
        if (!cancelled) setLoading(false)
      })

    return () => {
      cancelled = true
    }
  }, [auth, requestId])

  // The technician picker only makes sense once there is something to assign.
  useEffect(() => {
    if (!auth || !canAssign || request?.status !== 'Approved') return
    let cancelled = false

    getTechnicians(auth.token)
      .then(data => {
        if (!cancelled) setTechnicians(data)
      })
      .catch(() => {
        // Falls back to whatever the select already shows; the assign action itself will still fail
        // loudly through the guarded action if a technician is somehow submitted regardless.
      })

    return () => {
      cancelled = true
    }
  }, [auth, canAssign, request?.status])

  function handleApprove() {
    if (!auth || !request) return
    void run(
      () => approveServiceRequest(auth.token, requestId, request.rowVersion),
      updated => setRequest(updated),
      'Could not save your decision.'
    )
  }

  function handleReject(event: FormEvent) {
    event.preventDefault()
    if (!auth || !request) return
    void run(
      () => rejectServiceRequest(auth.token, requestId, reason.trim(), request.rowVersion),
      updated => {
        setRequest(updated)
        setRejecting(false)
        setReason('')
      },
      'Could not save your decision.'
    )
  }

  function handleCancel(event: FormEvent) {
    event.preventDefault()
    if (!auth || !request) return
    void run(
      () => cancelServiceRequest(auth.token, requestId, cancelReason.trim(), request.rowVersion),
      updated => {
        setRequest(updated)
        setCancelling(false)
        setCancelReason('')
      },
      'Could not cancel this request.'
    )
  }

  function handleAssign(event: FormEvent) {
    event.preventDefault()
    if (!auth || !request || technicianId === '') return
    void run(
      async () => {
        await assignServiceRequest(auth.token, requestId, technicianId, request.rowVersion)
        return reload()
      },
      updated => {
        setAssigning(false)
        setRequest(updated)
      },
      'Could not assign this job.'
    )
  }

  if (loading) return <p className="page-subtitle">Loading service request…</p>
  if (error || !request) {
    return (
      <div>
        <p className="form-error">{error ?? 'Service request not found.'}</p>
        <Link to="/service-requests">← Back to service requests</Link>
      </div>
    )
  }

  // A New request may be cancelled by the creator-side roles or a Manager; once Approved, only a
  // Manager can still cancel it (decision: cancelling is not just "my own mistake" any more).
  const canCancel =
    (request.status === 'New' && hasRole('Admin', 'Manager', 'ServiceStaff')) ||
    (request.status === 'Approved' && hasRole('Manager'))

  return (
    <div>
      <p className="breadcrumb"><Link to="/service-requests">← Service requests</Link></p>
      <h1 className="page-title">
        Service request #{request.id} <StatusBadge status={request.status} />
      </h1>

      <dl className="detail-list panel">
        <div>
          <dt>Customer</dt>
          <dd><Link to={`/customers/${request.customerId}`}>{request.customerName}</Link></dd>
        </div>
        <div>
          <dt>Equipment</dt>
          <dd>
            <Link to={`/equipment/${request.equipmentId}`}>
              <span className="mono">{request.equipmentSerialNumber}</span>
            </Link>{' '}
            · {request.equipmentType}
          </dd>
        </div>
        <div><dt>Priority</dt><dd><PriorityBadge priority={request.priority} /></dd></div>
        <div><dt>Problem</dt><dd className="prose">{request.problemDescription}</dd></div>
      </dl>

      <StatusTimeline
        entries={[
          { label: 'Raised', at: request.createdAt, by: request.createdByName },
          { label: 'Approved', at: request.approvedAt, by: request.approvedByName },
          { label: 'Rejected', at: request.rejectedAt, by: request.rejectedByName, detail: request.rejectionReason },
          { label: 'Cancelled', at: request.cancelledAt, by: request.cancelledByName, detail: request.cancellationReason }
        ]}
      />

      <ConflictBanner message={actionError} />

      {request.status === 'New' && canReview && !cancelling && (
        <div className="panel">
          <h2 className="panel-title">Review this request</h2>

          {rejecting ? (
            <form onSubmit={handleReject}>
              <div className="field">
                <label className="field-label" htmlFor="reject-reason">Reason for rejecting</label>
                <textarea
                  id="reject-reason"
                  aria-describedby="reject-reason-hint"
                  value={reason}
                  onChange={e => setReason(e.target.value)}
                  maxLength={500}
                  rows={3}
                  required
                  autoFocus
                />
                <span id="reject-reason-hint" className="form-hint">
                  The person who raised the request will see this reason.
                </span>
              </div>
              <div className="panel-actions">
                <button type="button" className="btn btn-ghost" onClick={() => setRejecting(false)} disabled={busy}>
                  Back
                </button>
                <button type="submit" className="btn btn-primary" disabled={busy || reason.trim() === ''}>
                  {busy ? 'Rejecting…' : 'Confirm rejection'}
                </button>
              </div>
            </form>
          ) : (
            <div className="panel-actions">
              <button className="btn btn-ghost" onClick={() => setRejecting(true)} disabled={busy}>
                Reject
              </button>
              <button className="btn btn-primary" onClick={handleApprove} disabled={busy}>
                {busy ? 'Approving…' : 'Approve'}
              </button>
            </div>
          )}
        </div>
      )}

      {request.status === 'New' && !canReview && (
        <p className="form-hint">Waiting for a Manager to review this request.</p>
      )}

      {request.status === 'Approved' && canAssign && !cancelling && (
        <div className="panel">
          <h2 className="panel-title">Assign a technician</h2>

          {assigning ? (
            <form onSubmit={handleAssign}>
              <label className="field">
                <span className="field-label">Technician</span>
                <select
                  value={technicianId}
                  onChange={e => setTechnicianId(e.target.value)}
                  required
                  autoFocus
                >
                  <option value="">Select a technician…</option>
                  {technicians.map(t => (
                    <option key={t.id} value={t.id}>
                      {t.fullName} ({t.openJobCount} open job{t.openJobCount === 1 ? '' : 's'})
                    </option>
                  ))}
                </select>
              </label>
              <div className="panel-actions">
                <button type="button" className="btn btn-ghost" onClick={() => setAssigning(false)} disabled={busy}>
                  Cancel
                </button>
                <button type="submit" className="btn btn-primary" disabled={busy || technicianId === ''}>
                  {busy ? 'Assigning…' : 'Assign'}
                </button>
              </div>
            </form>
          ) : (
            <div className="panel-actions">
              <button className="btn btn-primary" onClick={() => setAssigning(true)} disabled={busy}>
                Assign to a technician
              </button>
            </div>
          )}
        </div>
      )}

      {request.status === 'Approved' && !canAssign && (
        <p className="form-hint">Waiting for a Manager to assign this job to a technician.</p>
      )}

      {request.status === 'Assigned' && (
        request.workOrderId ? (
          <p className="form-notice" role="status">
            Assigned. <Link to={`/work-orders/${request.workOrderId}`}>View the work order</Link>
          </p>
        ) : (
          <p className="form-hint">
            This request has been assigned to a technician. See it on the <Link to="/work-orders">Work Orders</Link> page.
          </p>
        )
      )}

      {canCancel && !rejecting && !assigning && (
        <div className="panel">
          <h2 className="panel-title">Cancel this request</h2>

          {cancelling ? (
            <form onSubmit={handleCancel}>
              <div className="field">
                <label className="field-label" htmlFor="cancel-reason">Reason for cancelling</label>
                <textarea
                  id="cancel-reason"
                  value={cancelReason}
                  onChange={e => setCancelReason(e.target.value)}
                  maxLength={500}
                  rows={3}
                  required
                  autoFocus
                />
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
                Cancel request…
              </button>
            </div>
          )}
        </div>
      )}
    </div>
  )
}
