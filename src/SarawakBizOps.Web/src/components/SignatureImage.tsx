import { useAuth } from '../auth/AuthContext'
import { getSignature } from '../api/workOrders'
import { useProtectedImage } from '../hooks/useProtectedImage'

interface SignatureImageProps {
  workOrderId: number
  /** When the signature was stored; a new value (the customer signed again) fetches the new image. */
  signedAt?: string | null
}

/**
 * Shows a job's stored signature. The image has no public URL — it is fetched through the authorized API
 * with the bearer token and shown from an object URL, released again when the component goes away.
 */
export function SignatureImage({ workOrderId, signedAt }: SignatureImageProps) {
  const { auth } = useAuth()
  const { src, error } = useProtectedImage(
    auth ? () => getSignature(auth.token, workOrderId) : null,
    `${auth?.token}|${workOrderId}|${signedAt}`,
    'Could not load the signature.'
  )

  if (error) return <p className="form-error">{error}</p>
  if (!src) return <p className="form-hint">Loading signature…</p>
  return <img className="signature-image" src={src} alt="Customer signature" />
}
