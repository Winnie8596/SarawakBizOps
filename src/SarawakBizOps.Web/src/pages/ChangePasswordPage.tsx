import { useState, type FormEvent } from 'react'
import { useNavigate } from 'react-router-dom'
import { useAuth } from '../auth/AuthContext'
import { changePassword } from '../api/auth'
import { ApiError } from '../api/client'

export function ChangePasswordPage() {
  const { auth, logout } = useAuth()
  const navigate = useNavigate()

  const [current, setCurrent] = useState('')
  const [next, setNext] = useState('')
  const [confirm, setConfirm] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [saving, setSaving] = useState(false)

  async function handleSubmit(event: FormEvent) {
    event.preventDefault()
    if (!auth) return

    if (next !== confirm) {
      setError('The new password and its confirmation do not match.')
      return
    }

    setSaving(true)
    setError(null)
    try {
      await changePassword(auth.token, current, next)
      // The server rotates the security stamp, which ends every session including
      // this one, so send the user to sign in again with the new password.
      logout()
      navigate('/login', {
        replace: true,
        state: { message: 'Password changed. Please sign in with your new password.' }
      })
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Could not change your password.')
      setSaving(false)
    }
  }

  return (
    <div>
      <h1 className="page-title">Change password</h1>
      <p className="page-subtitle">You will be signed out afterwards and need to sign in again.</p>

      <form className="panel form-panel narrow" onSubmit={handleSubmit}>
        <label className="field">
          <span className="field-label">Current password</span>
          <input
            type="password"
            autoComplete="current-password"
            value={current}
            onChange={e => setCurrent(e.target.value)}
            required
          />
        </label>
        <label className="field">
          <span className="field-label">New password (min. 8 characters)</span>
          <input
            type="password"
            autoComplete="new-password"
            minLength={8}
            value={next}
            onChange={e => setNext(e.target.value)}
            required
          />
        </label>
        <label className="field">
          <span className="field-label">Confirm new password</span>
          <input
            type="password"
            autoComplete="new-password"
            value={confirm}
            onChange={e => setConfirm(e.target.value)}
            required
          />
        </label>

        {error && <p className="form-error">{error}</p>}

        <div className="panel-actions">
          <button type="submit" className="btn btn-primary" disabled={saving}>
            {saving ? 'Saving…' : 'Change password'}
          </button>
        </div>
      </form>
    </div>
  )
}
