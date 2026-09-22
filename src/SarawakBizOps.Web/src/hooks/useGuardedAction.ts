import { useState } from 'react'
import { ApiError } from '../api/client'

/**
 * Every detail page runs mutating actions the same way: show busy state, catch a failure, and on a
 * 409 (the record changed under us) reload so the UI matches what is really stored instead of
 * silently trusting stale local state. This used to be duplicated inline in every handler; Phase 5
 * centralizes it so the "record changed — refresh" behaviour is identical everywhere.
 */
export function useGuardedAction<T>(reload: () => Promise<T>) {
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  async function run(action: () => Promise<T>, onSuccess: (result: T) => void, fallbackMessage: string) {
    setBusy(true)
    setError(null)
    try {
      onSuccess(await action())
    } catch (err) {
      setError(err instanceof ApiError ? err.message : fallbackMessage)
      if (err instanceof ApiError && err.status === 409) {
        try {
          onSuccess(await reload())
        } catch {
          // The message above already explains the conflict; the page keeps its previous data.
        }
      }
    } finally {
      setBusy(false)
    }
  }

  return { busy, error, setError, run }
}
