import { useEffect, useState, type FormEvent } from 'react'
import { Link, useParams } from 'react-router-dom'
import { useAuth } from '../auth/AuthContext'
import { approveWorkOrder, cancelWorkOrder, getWorkOrder } from '../api/workOrders'
import { generateReport } from '../api/reports'
import { ApiError } from '../api/client'
import type { WorkOrder } from '../types'
import { PriorityBadge } from '../components/PriorityBadge'
import { StatusBadge } from '../components/StatusBadge'
import { ConflictBanner } from '../components/ConflictBanner'
import { StatusTimeline } from '../components/StatusTimeline'
import { SignatureImage } from '../components/SignatureImage'
import { JobPhotoGallery } from '../components/JobPhotoGallery'
import { ServiceReportPanel } from '../components/ServiceReportPanel'
import { AssistantPanel } from '../components/AssistantPanel'
import { useGuardedAction } from '../hooks/useGuardedAction'
import { formatDateTime, formatMoney, formatQuantity } from '../utils/format'

const CANCELLABLE_STATUSES = ['New', 'Assigned', 'InProgress', 'PendingParts']
// Phase 13: the AI assistant helps while the job is still being diagnosed.
const ASSISTANT_STATUSES = ['Assigned', 'InProgress', 'PendingParts']

export function WorkOrderDetailPage() {
  const { auth, hasRole } = useAuth()
  const { id } = useParams()
  const workOrderId = Number(id)
  const canApprove = hasRole('Manager')
  const canCancelJob = hasRole('Manager')
  const canGenerateReport = hasRole('Manager')
  const canAskAssistant = hasRole('Manager')

  const [workOrder, setWorkOrder] = useState<WorkOrder | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  const [cancelling, setCancelling] = useState(false)
  const [cancelReason, setCancelReason] = useState('')
  const [generating, setGenerating] = useState(false)

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

  // Returns the job with its report; a 409 (e.g. approved meanwhile, report locked) reloads it.
  async function handleGenerateReport() {
    if (!auth) return
    setGenerating(true)
    await run(
      () => generateReport(auth.token, workOrderId),
      updated => setWorkOrder(updated),
      'Could not generate the service report.'
    )
    setGenerating(false)
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

      {canAskAssistant && ASSISTANT_STATUSES.includes(workOrder.status) && (
        <AssistantPanel workOrderId={workOrder.id} linkPastCases />
      )}

      {workOrder.status === 'PendingParts' && (
        <div className="panel">
          <h2 className="panel-title">Waiting for parts</h2>
          <dl className="detail-list">
            <div>
              <dt>Needs</dt>
              <dd>
                {formatQuantity(workOrder.pendingPartQuantity ?? 0)} {workOrder.pendingPartUnit} ·{' '}
                <span className="mono">{workOrder.pendingPartNumber}</span> {workOrder.pendingPartName}
              </dd>
            </div>
            {workOrder.pendingPartsReason && (
              <div><dt>Reason</dt><dd className="prose">{workOrder.pendingPartsReason}</dd></div>
            )}
          </dl>
          <p className="form-hint">The technician can resume once the part has been received into stock.</p>
        </div>
      )}

      <div className="panel">
        <h2 className="panel-title">Parts used</h2>
        {workOrder.parts.length === 0 ? (
          <p className="form-hint">No parts used on this job.</p>
        ) : (
          <div className="table-scroll">
            <table className="data-table">
              <thead>
                <tr>
                  <th>Part</th>
                  <th className="num">Quantity</th>
                  <th className="num">Unit price</th>
                  <th className="num">Line total</th>
                </tr>
              </thead>
              <tbody>
                {workOrder.parts.map(line => (
                  <tr key={line.id}>
                    <td><span className="mono">{line.partNumber}</span> {line.partName}</td>
                    <td className="num">{formatQuantity(line.quantity)} {line.unit}</td>
                    <td className="num">{formatMoney(line.unitPrice)}</td>
                    <td className="num">{formatMoney(line.lineTotal)}</td>
                  </tr>
                ))}
              </tbody>
              <tfoot>
                <tr>
                  <th colSpan={3}>Parts total</th>
                  <td className="num"><strong>{formatMoney(workOrder.partsTotal)}</strong></td>
                </tr>
              </tfoot>
            </table>
          </div>
        )}
      </div>

      <div className="panel">
        <h2 className="panel-title">Photos</h2>
        {workOrder.photos.length === 0 ? (
          <p className="form-hint">No photos taken on this job.</p>
        ) : (
          <JobPhotoGallery workOrderId={workOrder.id} photos={workOrder.photos} />
        )}
      </div>

      {(workOrder.hasSignature || workOrder.status !== 'Cancelled') && (
        <div className="panel">
          <h2 className="panel-title">Customer signature</h2>
          {workOrder.hasSignature ? (
            <>
              <SignatureImage workOrderId={workOrder.id} signedAt={workOrder.signedAt} />
              {workOrder.signedAt && <p className="form-hint">Signed {formatDateTime(workOrder.signedAt)}</p>}
            </>
          ) : (
            <p className="form-hint">Not signed yet. The customer signs on the technician’s phone before the job is completed.</p>
          )}
        </div>
      )}

      {(workOrder.status === 'Completed' || workOrder.status === 'Approved' || workOrder.report) && (
        <ServiceReportPanel
          workOrder={workOrder}
          canGenerate={canGenerateReport}
          generating={generating}
          busy={busy}
          onGenerate={() => void handleGenerateReport()}
        />
      )}

      <StatusTimeline
        entries={[
          { label: 'Assigned', at: workOrder.assignedAt },
          { label: 'Started', at: workOrder.startedAt },
          { label: 'Waiting for parts', at: workOrder.pendingPartsAt, detail: workOrder.pendingPartsReason },
          { label: 'Resumed', at: workOrder.resumedAt },
          { label: 'Customer signed', at: workOrder.signedAt },
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
            {busy && !generating ? 'Approving…' : 'Approve'}
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
