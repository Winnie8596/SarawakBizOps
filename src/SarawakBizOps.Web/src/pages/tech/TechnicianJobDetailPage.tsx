import { useEffect, useState, type FormEvent } from 'react'
import { Link, useParams } from 'react-router-dom'
import { useAuth } from '../../auth/AuthContext'
import {
  addWorkOrderPart,
  completeWorkOrder,
  getWorkOrder,
  markPendingParts,
  removeWorkOrderPhoto,
  resumeWorkOrder,
  returnWorkOrderPart,
  saveDiagnosis,
  startWorkOrder,
  uploadSignature
} from '../../api/workOrders'
import { ApiError } from '../../api/client'
import type { WorkOrder, WorkOrderPart, WorkOrderPhoto } from '../../types'
import { PriorityBadge } from '../../components/PriorityBadge'
import { StatusBadge } from '../../components/StatusBadge'
import { ConflictBanner } from '../../components/ConflictBanner'
import { useGuardedAction } from '../../hooks/useGuardedAction'
import { formatDateTime, formatQuantity } from '../../utils/format'
import { SignaturePad } from '../../components/SignaturePad'
import { SignatureImage } from '../../components/SignatureImage'
import { JobPartsSection } from './JobPartsSection'
import { JobPhotosSection } from './JobPhotosSection'
import { JobReportButton } from './JobReportButton'
import { AssistantPanel } from '../../components/AssistantPanel'

const NOTES_MAX_LENGTH = 2000

// One screen, few taps (NFR-04): Start; add parts; take photos; type the diagnosis and work performed; the
// customer signs; Complete. The sticky bottom action bar keeps the primary next step reachable with one thumb on a 360 px screen.
export function TechnicianJobDetailPage() {
  const { auth } = useAuth()
  const { id } = useParams()
  const jobId = Number(id)

  const [job, setJob] = useState<WorkOrder | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  const [diagnosis, setDiagnosis] = useState('')
  const [workPerformed, setWorkPerformed] = useState('')
  const [notes, setNotes] = useState('')
  // True while the customer signs again over an already stored signature.
  const [resigning, setResigning] = useState(false)

  const reload = () => getWorkOrder(auth!.token, jobId)
  const { busy: actionBusy, error: actionError, run } = useGuardedAction(reload)
  // Photo uploads carry the job's rowVersion forward one after another, so every other action waits for them.
  const [photosUploading, setPhotosUploading] = useState(false)
  const busy = actionBusy || photosUploading

  useEffect(() => {
    if (!auth) return
    let cancelled = false

    setLoading(true)
    setError(null)
    getWorkOrder(auth.token, jobId)
      .then(data => {
        if (cancelled) return
        setJob(data)
        setDiagnosis(data.diagnosis ?? '')
        setWorkPerformed(data.workPerformed ?? '')
        setNotes(data.notes ?? '')
      })
      .catch(err => {
        if (!cancelled) setError(err instanceof ApiError ? err.message : 'Could not load this job.')
      })
      .finally(() => {
        if (!cancelled) setLoading(false)
      })

    return () => {
      cancelled = true
    }
  }, [auth, jobId])

  function handleStart() {
    if (!auth || !job) return
    void run(
      () => startWorkOrder(auth.token, jobId, job.rowVersion),
      updated => setJob(updated),
      'Could not start this job.'
    )
  }

  // Resolves true only when the API call itself succeeded (not when a 409 merely reloaded the job), so the
  // part picker stays open after a refusal and the technician can pick again or choose to wait.
  async function runSucceeded(action: () => Promise<WorkOrder>, fallbackMessage: string): Promise<boolean> {
    let succeeded = false
    await run(
      async () => {
        const updated = await action()
        succeeded = true
        return updated
      },
      updated => setJob(updated),
      fallbackMessage
    )
    return succeeded
  }

  function handleAddPart(partId: number, quantity: number) {
    return runSucceeded(
      () => addWorkOrderPart(auth!.token, jobId, partId, quantity, job!.rowVersion),
      'Could not add this part.'
    )
  }

  function handleReturnPart(line: WorkOrderPart) {
    void runSucceeded(
      () => returnWorkOrderPart(auth!.token, jobId, line.id, job!.rowVersion),
      'Could not return this part.'
    )
  }

  function handleWaitForPart(partId: number, quantity: number, reason: string) {
    return runSucceeded(
      () => markPendingParts(auth!.token, jobId, { reason, partId, quantity, rowVersion: job!.rowVersion }),
      'Could not pause this job.'
    )
  }

  // Stored straight away (not on Complete), so the phone can go back to the technician with the signature safe.
  async function handleSaveSignature(png: Blob) {
    const saved = await runSucceeded(
      () => uploadSignature(auth!.token, jobId, png, job!.rowVersion),
      'Could not save the signature.'
    )
    if (saved) setResigning(false)
    return saved
  }

  // Phase 13: the assistant's suggestions go into the notes field, editable and unsaved until the job is — never
  // into the diagnosis, which stays the technician's own finding.
  function handleCopyToNotes(text: string) {
    setNotes(previous => (previous.trim() ? `${previous.trimEnd()}\n\n${text}` : text).slice(0, NOTES_MAX_LENGTH))
  }

  function handleRemovePhoto(photo: WorkOrderPhoto) {
    void runSucceeded(
      () => removeWorkOrderPhoto(auth!.token, jobId, photo.id, job!.rowVersion),
      'Could not remove this photo.'
    )
  }

  async function reloadAfterPhotoConflict(): Promise<WorkOrder | null> {
    try {
      const fresh = await reload()
      setJob(fresh)
      return fresh
    } catch {
      return null
    }
  }

  function handleResume() {
    if (!auth || !job) return
    void run(
      () => resumeWorkOrder(auth.token, jobId, job.rowVersion),
      updated => setJob(updated),
      'Could not resume this job.'
    )
  }

  // Saves the diagnosis and work performed, then completes in the same tap — the technician only
  // ever presses one button, even though the API models them as two steps.
  async function handleComplete(event: FormEvent) {
    event.preventDefault()
    if (!auth || !job) return
    await run(
      async () => {
        const saved = await saveDiagnosis(auth.token, jobId, {
          diagnosis: diagnosis.trim(),
          workPerformed: workPerformed.trim(),
          notes: notes.trim() || undefined,
          rowVersion: job.rowVersion
        })
        return completeWorkOrder(auth.token, jobId, saved.rowVersion)
      },
      updated => setJob(updated),
      'Could not complete this job.'
    )
  }

  if (loading) return <p className="page-subtitle">Loading job…</p>
  if (error || !job) {
    return (
      <div>
        <p className="form-error">{error ?? 'Job not found.'}</p>
        <Link to="/tech/jobs">← Back to My Jobs</Link>
      </div>
    )
  }

  return (
    <div>
      <p className="breadcrumb"><Link to="/tech/jobs">← My Jobs</Link></p>
      <h1 className="page-title">
        <span className="mono">{job.equipmentSerialNumber}</span> <StatusBadge status={job.status} />
      </h1>
      <p className="page-subtitle">{job.customerName} · {job.equipmentType}</p>

      <dl className="detail-list panel">
        <div><dt>Priority</dt><dd><PriorityBadge priority={job.priority} /></dd></div>
        <div><dt>Problem</dt><dd className="prose">{job.problemDescription}</dd></div>
        {job.assignedAt && <div><dt>Assigned</dt><dd>{formatDateTime(job.assignedAt)}</dd></div>}
        {job.startedAt && <div><dt>Started</dt><dd>{formatDateTime(job.startedAt)}</dd></div>}
        {job.completedAt && <div><dt>Completed</dt><dd>{formatDateTime(job.completedAt)}</dd></div>}
        {job.approvedAt && <div><dt>Approved</dt><dd>{formatDateTime(job.approvedAt)}</dd></div>}
      </dl>

      <ConflictBanner message={actionError} />

      {job.status === 'Assigned' && (
        <div className="tech-action-bar">
          <button className="btn btn-primary tech-primary-btn" onClick={handleStart} disabled={busy}>
            {busy ? 'Starting…' : 'Start job'}
          </button>
        </div>
      )}

      {job.status === 'PendingParts' && (
        <>
          <div className="panel">
            <h2 className="panel-title">Waiting for parts</h2>
            <dl className="detail-list">
              {job.pendingPartNumber && (
                <div>
                  <dt>Needs</dt>
                  <dd>
                    {formatQuantity(job.pendingPartQuantity ?? 0)} {job.pendingPartUnit} ·{' '}
                    <span className="mono">{job.pendingPartNumber}</span> {job.pendingPartName}
                  </dd>
                </div>
              )}
              {job.pendingPartsReason && <div><dt>Reason</dt><dd className="prose">{job.pendingPartsReason}</dd></div>}
              {job.pendingPartsAt && <div><dt>Since</dt><dd>{formatDateTime(job.pendingPartsAt)}</dd></div>}
            </dl>
            <p className="form-hint">Resume once the store has received the part.</p>
          </div>
          <div className="tech-action-bar">
            <button className="btn btn-primary tech-primary-btn" onClick={handleResume} disabled={busy}>
              {busy ? 'Resuming…' : 'Resume job'}
            </button>
          </div>
        </>
      )}

      {job.status !== 'Assigned' && (
        <JobPartsSection
          parts={job.parts}
          partsTotal={job.partsTotal}
          editable={job.status === 'InProgress'}
          busy={busy}
          onAdd={handleAddPart}
          onReturn={handleReturnPart}
          onWait={handleWaitForPart}
        />
      )}

      {job.status !== 'Assigned' && (
        <JobPhotosSection
          job={job}
          editable={job.status === 'InProgress'}
          busy={actionBusy}
          onUploaded={updated => setJob(updated)}
          onRemove={handleRemovePhoto}
          onConflict={reloadAfterPhotoConflict}
          onUploadingChange={setPhotosUploading}
        />
      )}

      {job.status === 'InProgress' && <AssistantPanel workOrderId={job.id} onCopyToNotes={handleCopyToNotes} />}

      {job.status === 'InProgress' && (
        <form onSubmit={handleComplete}>
          <div className="panel">
            <h2 className="panel-title">Diagnosis and work performed</h2>
            <div className="field">
              <label className="field-label" htmlFor="job-diagnosis">What is wrong?</label>
              <textarea
                id="job-diagnosis"
                value={diagnosis}
                onChange={e => setDiagnosis(e.target.value)}
                rows={3}
                maxLength={4000}
                required
              />
            </div>
            <div className="field">
              <label className="field-label" htmlFor="job-work-performed">What did you do?</label>
              <textarea
                id="job-work-performed"
                value={workPerformed}
                onChange={e => setWorkPerformed(e.target.value)}
                rows={3}
                maxLength={4000}
                required
              />
            </div>
            <div className="field">
              <label className="field-label" htmlFor="job-notes">Notes (optional)</label>
              <textarea
                id="job-notes"
                value={notes}
                onChange={e => setNotes(e.target.value)}
                rows={2}
                maxLength={NOTES_MAX_LENGTH}
              />
            </div>
          </div>

          <div className="panel">
            <h2 className="panel-title">Customer signature</h2>
            {job.hasSignature && !resigning ? (
              <>
                <SignatureImage workOrderId={job.id} signedAt={job.signedAt} />
                {job.signedAt && <p className="form-hint">Signed {formatDateTime(job.signedAt)}</p>}
                <button type="button" className="btn btn-ghost tech-secondary-btn" onClick={() => setResigning(true)} disabled={busy}>
                  Sign again
                </button>
              </>
            ) : (
              <>
                <p className="form-hint">Hand the phone to the customer to sign for the work done.</p>
                <SignaturePad
                  onSave={handleSaveSignature}
                  busy={busy}
                  onCancel={job.hasSignature ? () => setResigning(false) : undefined}
                />
              </>
            )}
          </div>

          <div className="tech-action-bar">
            {!job.hasSignature && (
              <p className="form-hint tech-action-hint">The customer must sign before the job can be completed.</p>
            )}
            <button
              type="submit"
              className="btn btn-primary tech-primary-btn"
              disabled={busy || diagnosis.trim() === '' || workPerformed.trim() === '' || !job.hasSignature}
            >
              {actionBusy ? 'Completing…' : photosUploading ? 'Uploading photos…' : 'Complete job'}
            </button>
          </div>
        </form>
      )}

      {(job.status === 'Completed' || job.status === 'Approved') && (
        <div className="panel">
          <h2 className="panel-title">
            {job.status === 'Approved' ? 'Approved — read only' : 'Completed — waiting for Manager approval'}
          </h2>
          <dl className="detail-list">
            <div><dt>Diagnosis</dt><dd className="prose">{job.diagnosis}</dd></div>
            <div><dt>Work performed</dt><dd className="prose">{job.workPerformed}</dd></div>
            {job.notes && <div><dt>Notes</dt><dd className="prose">{job.notes}</dd></div>}
          </dl>
          {job.hasSignature && (
            <>
              <h3 className="panel-subtitle">Customer signature</h3>
              <SignatureImage workOrderId={job.id} signedAt={job.signedAt} />
            </>
          )}
          <JobReportButton job={job} />
        </div>
      )}

      {job.status === 'Cancelled' && (
        <div className="panel">
          <h2 className="panel-title">Cancelled — read only</h2>
          <dl className="detail-list">
            {job.cancelledAt && <div><dt>Cancelled</dt><dd>{formatDateTime(job.cancelledAt)}</dd></div>}
            {job.cancellationReason && (
              <div><dt>Reason</dt><dd className="prose">{job.cancellationReason}</dd></div>
            )}
          </dl>
        </div>
      )}
    </div>
  )
}
