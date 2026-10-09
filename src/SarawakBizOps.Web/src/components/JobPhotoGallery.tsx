import { useAuth } from '../auth/AuthContext'
import { getWorkOrderPhoto } from '../api/workOrders'
import { useProtectedImage } from '../hooks/useProtectedImage'
import type { WorkOrderPhoto } from '../types'
import { formatDateTime } from '../utils/format'

interface JobPhotoGalleryProps {
  workOrderId: number
  photos: WorkOrderPhoto[]
  /** When given (the technician, while the job is in progress), each photo gets a Remove button. */
  onRemove?: (photo: WorkOrderPhoto) => void
  busy?: boolean
}

/** Thumbnails of a job's photos; tapping one opens it full size in a new tab. */
export function JobPhotoGallery({ workOrderId, photos, onRemove, busy = false }: JobPhotoGalleryProps) {
  return (
    <ul className="photo-grid">
      {photos.map((photo, index) => (
        <li key={photo.id} className="photo-tile">
          <PhotoThumbnail workOrderId={workOrderId} photo={photo} label={`Job photo ${index + 1}`} />
          <span className="photo-meta">{formatDateTime(photo.uploadedAt)}</span>
          {onRemove && (
            <button
              type="button"
              className="btn btn-ghost photo-remove"
              onClick={() => onRemove(photo)}
              disabled={busy}
              aria-label={`Remove job photo ${index + 1}`}
            >
              Remove
            </button>
          )}
        </li>
      ))}
    </ul>
  )
}

function PhotoThumbnail({ workOrderId, photo, label }: { workOrderId: number; photo: WorkOrderPhoto; label: string }) {
  const { auth } = useAuth()
  const { src, error } = useProtectedImage(
    auth ? () => getWorkOrderPhoto(workOrderId, photo.id) : null,
    `${auth?.userId}|${workOrderId}|${photo.id}`,
    'Could not load this photo.'
  )

  if (error) return <p className="form-error photo-thumb photo-thumb-message">{error}</p>
  if (!src) return <p className="form-hint photo-thumb photo-thumb-message">Loading…</p>
  return (
    <a href={src} target="_blank" rel="noopener noreferrer" title="Open full size">
      <img className="photo-thumb" src={src} alt={label} />
    </a>
  )
}
