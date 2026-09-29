import { useEffect, useRef, useState, type ChangeEvent } from 'react'
import { useAuth } from '../../auth/AuthContext'
import { uploadWorkOrderPhoto } from '../../api/workOrders'
import { ApiError } from '../../api/client'
import type { WorkOrder, WorkOrderPhoto } from '../../types'
import { JobPhotoGallery } from '../../components/JobPhotoGallery'
import { shrinkImageIfNeeded } from '../../utils/shrinkImage'

/** The API's per-job limit (PhotoRules.MaxPhotosPerJob). */
const MAX_PHOTOS_PER_JOB = 20

interface QueuedPhoto {
  key: number
  file: File
  preview: string
  status: 'waiting' | 'uploading' | 'failed'
  progress: number
  error?: string
}

interface JobPhotosSectionProps {
  job: WorkOrder
  /** True while the job is InProgress: photos can be added and removed. */
  editable: boolean
  /** Another action on the job is running. */
  busy: boolean
  onUploaded: (updated: WorkOrder) => void
  onRemove: (photo: WorkOrderPhoto) => void
  /** A 409 (the job changed under us): reload it, and hand back the fresh copy for the next upload. */
  onConflict: () => Promise<WorkOrder | null>
  /** Lets the page hold back its other actions while uploads carry the job's rowVersion forward. */
  onUploadingChange: (uploading: boolean) => void
}

/**
 * The technician's Photos step (Phase 9). Photos picked or taken together are queued and uploaded one by one
 * — each upload carries the rowVersion the previous one returned (the Phase 5 rule for every mutating call) —
 * with a progress bar each, and a retry or discard when one fails. Stored photos show as thumbnails that can
 * be removed until the job is completed.
 */
export function JobPhotosSection({
  job, editable, busy, onUploaded, onRemove, onConflict, onUploadingChange
}: JobPhotosSectionProps) {
  const { auth } = useAuth()
  const [queue, setQueueState] = useState<QueuedPhoto[]>([])
  const queueRef = useRef<QueuedPhoto[]>([])
  const nextKey = useRef(1)
  const running = useRef(false)
  const rowVersion = useRef(job.rowVersion)
  // Between uploads the page's copy of the job is the source of truth (a Remove or a reload moves it on).
  if (!running.current) rowVersion.current = job.rowVersion

  function setQueue(change: (items: QueuedPhoto[]) => QueuedPhoto[]) {
    queueRef.current = change(queueRef.current)
    setQueueState(queueRef.current)
  }

  function patch(key: number, changes: Partial<QueuedPhoto>) {
    setQueue(items => items.map(item => (item.key === key ? { ...item, ...changes } : item)))
  }

  function discard(key: number) {
    const item = queueRef.current.find(i => i.key === key)
    if (item) URL.revokeObjectURL(item.preview)
    setQueue(items => items.filter(i => i.key !== key))
  }

  // Release every preview when the technician leaves the page.
  useEffect(() => () => queueRef.current.forEach(item => URL.revokeObjectURL(item.preview)), [])

  async function drain() {
    if (running.current || !auth) return
    running.current = true
    onUploadingChange(true)
    try {
      for (;;) {
        const next = queueRef.current.find(item => item.status === 'waiting')
        if (!next) break

        patch(next.key, { status: 'uploading', progress: 0, error: undefined })
        try {
          const photo = await shrinkImageIfNeeded(next.file)
          const updated = await uploadWorkOrderPhoto(
            auth.token, job.id, photo, rowVersion.current, fraction => patch(next.key, { progress: fraction })
          )
          rowVersion.current = updated.rowVersion
          onUploaded(updated)
          discard(next.key)
        } catch (err) {
          patch(next.key, {
            status: 'failed',
            error: err instanceof ApiError ? err.message : 'Could not upload this photo.'
          })
          if (err instanceof ApiError && err.status === 409) {
            const fresh = await onConflict()
            if (fresh) rowVersion.current = fresh.rowVersion
          }
        }
      }
    } finally {
      running.current = false
      onUploadingChange(false)
    }
  }

  function handlePicked(event: ChangeEvent<HTMLInputElement>) {
    const files = Array.from(event.target.files ?? [])
    event.target.value = '' // picking the same photo again still fires a change
    if (files.length === 0) return

    setQueue(items => [
      ...items,
      ...files.map(file => ({
        key: nextKey.current++,
        file,
        preview: URL.createObjectURL(file),
        status: 'waiting' as const,
        progress: 0
      }))
    ])
    void drain()
  }

  function retry(key: number) {
    patch(key, { status: 'waiting', progress: 0, error: undefined })
    void drain()
  }

  const uploading = queue.some(item => item.status !== 'failed')
  const atLimit = job.photos.length + queue.length >= MAX_PHOTOS_PER_JOB
  const pickerDisabled = busy || atLimit

  return (
    <div className="panel">
      <h2 className="panel-title">Photos</h2>

      {job.photos.length === 0 && queue.length === 0 && (
        <p className="form-hint">
          {editable ? 'No photos yet. Photograph the fault and the finished repair.' : 'No photos were taken on this job.'}
        </p>
      )}

      {job.photos.length > 0 && (
        <JobPhotoGallery
          workOrderId={job.id}
          photos={job.photos}
          onRemove={editable ? onRemove : undefined}
          busy={busy || uploading}
        />
      )}

      {queue.length > 0 && (
        <ul className="photo-grid" aria-label="Photo uploads">
          {queue.map(item => (
            <li key={item.key} className="photo-tile">
              <img className="photo-thumb photo-thumb-pending" src={item.preview} alt={`New photo ${item.file.name}`} />
              {item.status === 'failed' ? (
                <>
                  <p className="form-error photo-meta" role="alert">{item.error}</p>
                  <div className="photo-actions">
                    <button type="button" className="btn btn-primary" onClick={() => retry(item.key)} disabled={busy || !editable}>
                      Retry
                    </button>
                    <button type="button" className="btn btn-ghost" onClick={() => discard(item.key)}>
                      Discard
                    </button>
                  </div>
                </>
              ) : (
                <>
                  <progress
                    className="photo-progress"
                    value={item.progress}
                    max={1}
                    aria-label={`Uploading ${item.file.name}`}
                  />
                  <span className="photo-meta">
                    {item.status === 'waiting' ? 'Waiting…' : `Uploading ${Math.round(item.progress * 100)}%`}
                  </span>
                </>
              )}
            </li>
          ))}
        </ul>
      )}

      {editable && (
        <>
          <div className="photo-pickers">
            {/* capture opens the rear camera straight away on a phone; the other input picks from the gallery. */}
            <label className={`btn btn-primary photo-picker${pickerDisabled ? ' is-disabled' : ''}`}>
              Take photo
              <input
                type="file"
                className="file-input-hidden"
                accept="image/jpeg,image/png,image/webp"
                capture="environment"
                onChange={handlePicked}
                disabled={pickerDisabled}
              />
            </label>
            <label className={`btn btn-ghost photo-picker${pickerDisabled ? ' is-disabled' : ''}`}>
              Choose photos
              <input
                type="file"
                className="file-input-hidden"
                accept="image/jpeg,image/png,image/webp"
                multiple
                onChange={handlePicked}
                disabled={pickerDisabled}
              />
            </label>
          </div>
          <p className="form-hint">
            {atLimit
              ? `A job holds at most ${MAX_PHOTOS_PER_JOB} photos. Remove one to add another.`
              : 'JPEG, PNG or WebP. Photos can be removed until the job is completed.'}
          </p>
        </>
      )}
    </div>
  )
}
