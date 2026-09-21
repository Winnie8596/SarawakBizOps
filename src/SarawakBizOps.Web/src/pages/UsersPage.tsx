import { useEffect, useState, type FormEvent } from 'react'
import { useAuth } from '../auth/AuthContext'
import { createUser, getUsers, resetUserPassword, updateUser } from '../api/users'
import { ApiError } from '../api/client'
import { ROLES, type CreateUserInput, type UserSummary } from '../types'
import { EmptyState } from '../components/EmptyState'
import { StatusBadge } from '../components/StatusBadge'

type Panel =
  | { kind: 'create' }
  | { kind: 'edit'; user: UserSummary }
  | { kind: 'reset'; user: UserSummary }
  | null

const emptyCreate: CreateUserInput = { fullName: '', email: '', password: '', role: 'ServiceStaff' }

export function UsersPage() {
  const { auth } = useAuth()

  const [users, setUsers] = useState<UserSummary[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [message, setMessage] = useState<string | null>(null)

  const [panel, setPanel] = useState<Panel>(null)
  const [createForm, setCreateForm] = useState<CreateUserInput>(emptyCreate)
  const [editName, setEditName] = useState('')
  const [editRole, setEditRole] = useState('')
  const [newPassword, setNewPassword] = useState('')
  const [formError, setFormError] = useState<string | null>(null)
  const [saving, setSaving] = useState(false)

  useEffect(() => {
    if (!auth) return
    loadUsers()
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [auth])

  async function loadUsers() {
    if (!auth) return
    setLoading(true)
    setError(null)
    try {
      setUsers(await getUsers(auth.token))
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Could not load users.')
    } finally {
      setLoading(false)
    }
  }

  function open(next: Panel) {
    setFormError(null)
    setMessage(null)
    if (next?.kind === 'create') setCreateForm(emptyCreate)
    if (next?.kind === 'edit') {
      setEditName(next.user.fullName)
      setEditRole(next.user.role)
    }
    if (next?.kind === 'reset') setNewPassword('')
    setPanel(next)
  }

  // Runs a save action, then refreshes the list. Keeps the three forms and the
  // activate/deactivate button on one error/loading path. Row buttons (inForm =
  // false) report at page level and leave any open panel alone.
  async function run(
    action: () => Promise<unknown>,
    successMessage: string,
    fallbackError: string,
    inForm = true
  ) {
    setSaving(true)
    setFormError(null)
    try {
      await action()
      if (inForm) setPanel(null)
      setMessage(successMessage)
      await loadUsers()
    } catch (err) {
      const text = err instanceof ApiError ? err.message : fallbackError
      if (inForm) setFormError(text)
      else setError(text)
    } finally {
      setSaving(false)
    }
  }

  function handleCreate(event: FormEvent) {
    event.preventDefault()
    if (!auth) return
    run(() => createUser(auth.token, createForm), `Created ${createForm.email}.`, 'Could not create this user.')
  }

  function handleEdit(event: FormEvent) {
    event.preventDefault()
    if (!auth || panel?.kind !== 'edit') return
    const target = panel.user
    run(
      () => updateUser(auth.token, target.id, { fullName: editName, role: editRole }),
      `Updated ${target.email}.`,
      'Could not update this user.'
    )
  }

  function handleReset(event: FormEvent) {
    event.preventDefault()
    if (!auth || panel?.kind !== 'reset') return
    const target = panel.user
    run(
      () => resetUserPassword(auth.token, target.id, newPassword),
      `Password reset for ${target.email}. Share the new password with them securely.`,
      'Could not reset this password.'
    )
  }

  function toggleActive(user: UserSummary) {
    if (!auth) return
    setError(null)
    setMessage(null)
    run(
      () => updateUser(auth.token, user.id, { isActive: !user.isActive }),
      `${user.email} ${user.isActive ? 'deactivated' : 'reactivated'}.`,
      'Could not change this user.',
      false
    )
  }

  return (
    <div>
      <div className="page-header">
        <div>
          <h1 className="page-title">Users</h1>
          <p className="page-subtitle">
            Staff accounts and their roles. Deactivating a user signs them out immediately.
          </p>
        </div>
        <button className="btn btn-primary" onClick={() => open({ kind: 'create' })}>
          New user
        </button>
      </div>

      {error && <p className="form-error">{error}</p>}
      {message && <p className="form-notice">{message}</p>}

      {panel?.kind === 'create' && (
        <form className="panel form-panel" onSubmit={handleCreate}>
          <h2 className="panel-title">New user</h2>
          <div className="field-grid">
            <label className="field">
              <span className="field-label">Full name</span>
              <input
                value={createForm.fullName}
                onChange={e => setCreateForm({ ...createForm, fullName: e.target.value })}
                required
              />
            </label>
            <label className="field">
              <span className="field-label">Email (used to sign in)</span>
              <input
                type="email"
                value={createForm.email}
                onChange={e => setCreateForm({ ...createForm, email: e.target.value })}
                required
              />
            </label>
            <label className="field">
              <span className="field-label">Initial password (min. 8 characters)</span>
              <input
                type="password"
                autoComplete="new-password"
                minLength={8}
                value={createForm.password}
                onChange={e => setCreateForm({ ...createForm, password: e.target.value })}
                required
              />
            </label>
            <label className="field">
              <span className="field-label">Role</span>
              <select
                value={createForm.role}
                onChange={e => setCreateForm({ ...createForm, role: e.target.value })}
              >
                {ROLES.map(role => <option key={role} value={role}>{role}</option>)}
              </select>
            </label>
          </div>
          {formError && <p className="form-error">{formError}</p>}
          <div className="panel-actions">
            <button type="button" className="btn btn-ghost" onClick={() => setPanel(null)}>Cancel</button>
            <button type="submit" className="btn btn-primary" disabled={saving}>
              {saving ? 'Saving…' : 'Create user'}
            </button>
          </div>
        </form>
      )}

      {panel?.kind === 'edit' && (
        <form className="panel form-panel" onSubmit={handleEdit}>
          <h2 className="panel-title">Edit {panel.user.email}</h2>
          <div className="field-grid">
            <label className="field">
              <span className="field-label">Full name</span>
              <input value={editName} onChange={e => setEditName(e.target.value)} required />
            </label>
            <label className="field">
              <span className="field-label">Role</span>
              <select value={editRole} onChange={e => setEditRole(e.target.value)}>
                {ROLES.map(role => <option key={role} value={role}>{role}</option>)}
              </select>
            </label>
          </div>
          {editRole !== panel.user.role && (
            <p className="form-hint">Changing the role signs this user out; they sign in again to get the new access.</p>
          )}
          {formError && <p className="form-error">{formError}</p>}
          <div className="panel-actions">
            <button type="button" className="btn btn-ghost" onClick={() => setPanel(null)}>Cancel</button>
            <button type="submit" className="btn btn-primary" disabled={saving}>
              {saving ? 'Saving…' : 'Save changes'}
            </button>
          </div>
        </form>
      )}

      {panel?.kind === 'reset' && (
        <form className="panel form-panel" onSubmit={handleReset}>
          <h2 className="panel-title">Reset password for {panel.user.email}</h2>
          <label className="field">
            <span className="field-label">New password (min. 8 characters)</span>
            <input
              type="password"
              autoComplete="new-password"
              minLength={8}
              value={newPassword}
              onChange={e => setNewPassword(e.target.value)}
              required
            />
          </label>
          <p className="form-hint">This also unlocks the account and signs the user out everywhere.</p>
          {formError && <p className="form-error">{formError}</p>}
          <div className="panel-actions">
            <button type="button" className="btn btn-ghost" onClick={() => setPanel(null)}>Cancel</button>
            <button type="submit" className="btn btn-primary" disabled={saving}>
              {saving ? 'Saving…' : 'Reset password'}
            </button>
          </div>
        </form>
      )}

      {loading ? (
        <p className="page-subtitle">Loading users…</p>
      ) : users.length === 0 ? (
        <EmptyState title="No users" description="Create the first user account." />
      ) : (
        <table className="data-table">
          <thead>
            <tr>
              <th>Name</th>
              <th>Email</th>
              <th>Role</th>
              <th>Status</th>
              <th aria-label="Actions" />
            </tr>
          </thead>
          <tbody>
            {users.map(user => {
              const isSelf = user.id === auth?.userId
              return (
                <tr key={user.id}>
                  <td>{user.fullName}{isSelf && <span className="you-tag"> (you)</span>}</td>
                  <td>{user.email}</td>
                  <td>{user.role}</td>
                  <td><StatusBadge status={user.isActive ? 'Active' : 'Inactive'} /></td>
                  <td className="row-actions">
                    {/* The API refuses self-demotion/deactivation, so the buttons are hidden for your own row. */}
                    {!isSelf && (
                      <button className="btn btn-link" onClick={() => open({ kind: 'edit', user })}>Edit</button>
                    )}
                    <button className="btn btn-link" onClick={() => open({ kind: 'reset', user })}>Reset password</button>
                    {!isSelf && (
                      <button className="btn btn-link" onClick={() => toggleActive(user)} disabled={saving}>
                        {user.isActive ? 'Deactivate' : 'Reactivate'}
                      </button>
                    )}
                  </td>
                </tr>
              )
            })}
          </tbody>
        </table>
      )}
    </div>
  )
}
