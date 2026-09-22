import { useEffect, useState, type FormEvent } from 'react'
import { Link, useParams } from 'react-router-dom'
import { useAuth } from '../../auth/AuthContext'
import { completeWorkOrder, getWorkOrder, saveDiagnosis, startWorkOrder } from '../../api/workOrders'
import { ApiError } from '../../api/client'
import type { WorkOrder } from '../../types'
import { PriorityBadge } from '../../components/PriorityBadge'
import { StatusBadge } from '../../components/StatusBadge'
import { ConflictBanner } from '../../components/ConflictBanner'
import { useGuardedAction } from '../../hooks/useGuardedAction'
import { formatDateTime } from '../../utils/format'

// One screen, few taps (NFR-04): Start; type the diagnosis and work performed; Complete. The sticky
// bottom action bar keeps the primary next step reachable with one thumb on a 360 px screen.
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

  const reload = () => getWorkOrder(auth!.token, jobId)
  const { busy, error: actionError, run } = useGuardedAction(reload)

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
                maxLength={2000}
              />
            </div>
          </div>

          <div className="tech-action-bar">
            <button
              type="submit"
              className="btn btn-primary tech-primary-btn"
              disabled={busy || diagnosis.trim() === '' || workPerformed.trim() === ''}
            >
              {busy ? 'Completing…' : 'Complete job'}
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
