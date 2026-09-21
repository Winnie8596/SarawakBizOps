import { useEffect, useState, type FormEvent } from 'react'
import { Link, useParams } from 'react-router-dom'
import { useAuth } from '../auth/AuthContext'
import { approveServiceRequest, getServiceRequest, rejectServiceRequest } from '../api/serviceRequests'
import { ApiError } from '../api/client'
import type { ServiceRequest } from '../types'
import { PriorityBadge } from '../components/PriorityBadge'
import { StatusBadge } from '../components/StatusBadge'
import { formatDateTime } from '../utils/format'

export function ServiceRequestDetailPage() {
  const { auth, hasRole } = useAuth()
  const { id } = useParams()
  const requestId = Number(id)
  const canReview = hasRole('Manager')

  const [request, setRequest] = useState<ServiceRequest | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  const [rejecting, setRejecting] = useState(false)
  const [reason, setReason] = useState('')
  const [busy, setBusy] = useState(false)
  const [actionError, setActionError] = useState<string | null>(null)

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

  // Runs one review action. A 409 means someone else already decided (or the state moved on), so the
  // record is reloaded and the buttons then match what is really stored.
  async function review(action: () => Promise<ServiceRequest>) {
    if (!auth) return
    setBusy(true)
    setActionError(null)
    try {
      setRequest(await action())
      setRejecting(false)
      setReason('')
    } catch (err) {
      setActionError(err instanceof ApiError ? err.message : 'Could not save your decision.')
      if (err instanceof ApiError && err.status === 409) {
        setRejecting(false)
        try {
          setRequest(await getServiceRequest(auth.token, requestId))
        } catch {
          // The message above already explains the conflict; the page keeps its previous data.
        }
      }
    } finally {
      setBusy(false)
    }
  }

  function handleApprove() {
    if (!auth) return
    void review(() => approveServiceRequest(auth.token, requestId))
  }

  function handleReject(event: FormEvent) {
    event.preventDefault()
    if (!auth) return
    void review(() => rejectServiceRequest(auth.token, requestId, reason.trim()))
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
        <div>
          <dt>Raised</dt>
          <dd>{formatDateTime(request.createdAt)} by {request.createdByName}</dd>
        </div>
        {request.approvedAt && (
          <div>
            <dt>Approved</dt>
            <dd>{formatDateTime(request.approvedAt)} by {request.approvedByName}</dd>
          </div>
        )}
        {request.rejectedAt && (
          <>
            <div>
              <dt>Rejected</dt>
              <dd>{formatDateTime(request.rejectedAt)} by {request.rejectedByName}</dd>
            </div>
            <div><dt>Reason</dt><dd className="prose">{request.rejectionReason}</dd></div>
          </>
        )}
      </dl>

      {actionError && <p className="form-error" role="alert">{actionError}</p>}

      {request.status === 'New' && canReview && (
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
    </div>
  )
}
