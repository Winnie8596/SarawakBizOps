import { useEffect, useState } from 'react'
import { useAuth } from '../auth/AuthContext'
import { getSignature } from '../api/workOrders'
import { ApiError } from '../api/client'

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
  const [src, setSrc] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    if (!auth) return
    let cancelled = false
    let objectUrl: string | null = null

    setSrc(null)
    setError(null)
    getSignature(auth.token, workOrderId)
      .then(blob => {
        if (cancelled) return
        objectUrl = URL.createObjectURL(blob)
        setSrc(objectUrl)
      })
      .catch(err => {
        if (!cancelled) setError(err instanceof ApiError ? err.message : 'Could not load the signature.')
      })

    return () => {
      cancelled = true
      if (objectUrl) URL.revokeObjectURL(objectUrl)
    }
  }, [auth, workOrderId, signedAt])

  if (error) return <p className="form-error">{error}</p>
  if (!src) return <p className="form-hint">Loading signature…</p>
  return <img className="signature-image" src={src} alt="Customer signature" />
}
