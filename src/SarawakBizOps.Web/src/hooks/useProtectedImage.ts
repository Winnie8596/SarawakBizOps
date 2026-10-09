import { useEffect, useState } from 'react'
import { ApiError } from '../api/client'

/**
 * Loads an image that has no public URL (the signature, job photos): `load` fetches it through the
 * authorized API with the bearer token, and the result is shown from an object URL that is released again
 * when the component goes away or `key` changes (e.g. the customer signed again).
 */
export function useProtectedImage(load: (() => Promise<Blob>) | null, key: unknown, fallbackError: string) {
  const [src, setSrc] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    if (!load) return
    let cancelled = false
    let objectUrl: string | null = null

    setSrc(null)
    setError(null)
    load()
      .then(blob => {
        if (cancelled) return
        objectUrl = URL.createObjectURL(blob)
        setSrc(objectUrl)
      })
      .catch(err => {
        if (!cancelled) setError(err instanceof ApiError ? err.message : fallbackError)
      })

    return () => {
      cancelled = true
      if (objectUrl) URL.revokeObjectURL(objectUrl)
    }
    // `load` is a fresh closure every render; `key` names what it loads (and must change with the user).
    // eslint-disable-next-line react-hooks/exhaustive-deps -- deliberately keyed on `key`, not on the closures
  }, [key])

  return { src, error }
}
