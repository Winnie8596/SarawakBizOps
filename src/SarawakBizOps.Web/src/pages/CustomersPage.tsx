import { useEffect, useState, type FormEvent } from 'react'
import { Link } from 'react-router-dom'
import { useAuth } from '../auth/AuthContext'
import { createCustomer, getCustomers, updateCustomer } from '../api/customers'
import { ApiError } from '../api/client'
import type { Customer, CustomerInput } from '../types'
import { EmptyState } from '../components/EmptyState'

const emptyForm: CustomerInput = {
  companyName: '',
  contactPerson: '',
  phone: '',
  email: '',
  address: ''
}

export function CustomersPage() {
  const { auth, hasRole } = useAuth()
  const canEdit = hasRole('Admin', 'ServiceStaff')

  const [customers, setCustomers] = useState<Customer[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  const [editingId, setEditingId] = useState<number | 'new' | null>(null)
  const [form, setForm] = useState<CustomerInput>(emptyForm)
  const [formError, setFormError] = useState<string | null>(null)
  const [saving, setSaving] = useState(false)

  useEffect(() => {
    if (!auth) return
    loadCustomers()
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [auth])

  async function loadCustomers() {
    if (!auth) return
    setLoading(true)
    setError(null)
    try {
      const data = await getCustomers(auth.token)
      setCustomers(data)
    } catch {
      setError('Could not load customers.')
    } finally {
      setLoading(false)
    }
  }

  function startCreate() {
    setForm(emptyForm)
    setFormError(null)
    setEditingId('new')
  }

  function startEdit(customer: Customer) {
    setForm({
      companyName: customer.companyName,
      contactPerson: customer.contactPerson ?? '',
      phone: customer.phone ?? '',
      email: customer.email ?? '',
      address: customer.address ?? ''
    })
    setFormError(null)
    setEditingId(customer.id)
  }

  function closeForm() {
    setEditingId(null)
  }

  async function handleSubmit(event: FormEvent) {
    event.preventDefault()
    if (!auth || editingId === null) return

    setSaving(true)
    setFormError(null)

    try {
      if (editingId === 'new') {
        await createCustomer(auth.token, form)
      } else {
        await updateCustomer(auth.token, editingId, form)
      }
      closeForm()
      await loadCustomers()
    } catch (err) {
      setFormError(err instanceof ApiError ? err.message : 'Could not save this customer.')
    } finally {
      setSaving(false)
    }
  }

  return (
    <div>
      <div className="page-header">
        <div>
          <h1 className="page-title">Customers</h1>
          <p className="page-subtitle">Companies whose equipment your team services.</p>
        </div>
        {canEdit && (
          <button className="btn btn-primary" onClick={startCreate}>
            New customer
          </button>
        )}
      </div>

      {error && <p className="form-error">{error}</p>}

      {editingId !== null && (
        <form className="panel form-panel" onSubmit={handleSubmit}>
          <h2 className="panel-title">{editingId === 'new' ? 'New customer' : 'Edit customer'}</h2>

          <div className="field-grid">
            <label className="field">
              <span className="field-label">Company name</span>
              <input
                value={form.companyName}
                onChange={e => setForm({ ...form, companyName: e.target.value })}
                required
              />
            </label>

            <label className="field">
              <span className="field-label">Contact person</span>
              <input
                value={form.contactPerson}
                onChange={e => setForm({ ...form, contactPerson: e.target.value })}
              />
            </label>

            <label className="field">
              <span className="field-label">Phone</span>
              <input
                value={form.phone}
                onChange={e => setForm({ ...form, phone: e.target.value })}
              />
            </label>

            <label className="field">
              <span className="field-label">Email</span>
              <input
                type="email"
                value={form.email}
                onChange={e => setForm({ ...form, email: e.target.value })}
              />
            </label>

            <label className="field field-wide">
              <span className="field-label">Address</span>
              <input
                value={form.address}
                onChange={e => setForm({ ...form, address: e.target.value })}
              />
            </label>
          </div>

          {formError && <p className="form-error">{formError}</p>}

          <div className="panel-actions">
            <button type="button" className="btn btn-ghost" onClick={closeForm}>
              Cancel
            </button>
            <button type="submit" className="btn btn-primary" disabled={saving}>
              {saving ? 'Saving…' : 'Save customer'}
            </button>
          </div>
        </form>
      )}

      {loading ? (
        <p className="page-subtitle">Loading customers…</p>
      ) : customers.length === 0 ? (
        <EmptyState
          title="No customers yet"
          description={canEdit
            ? 'Add the first customer to start tracking their equipment.'
            : 'Ask a Service Staff member or Admin to add one.'}
        />
      ) : (
        <table className="data-table">
          <thead>
            <tr>
              <th>Company</th>
              <th>Contact</th>
              <th>Phone</th>
              <th>Email</th>
              <th aria-label="Actions" />
            </tr>
          </thead>
          <tbody>
            {customers.map(customer => (
              <tr key={customer.id}>
                <td><Link to={`/customers/${customer.id}`}>{customer.companyName}</Link></td>
                <td>{customer.contactPerson || '—'}</td>
                <td className="mono">{customer.phone || '—'}</td>
                <td>{customer.email || '—'}</td>
                <td className="row-actions">
                  <Link className="btn-link" to={`/customers/${customer.id}`}>View &amp; history</Link>
                  {canEdit && (
                    <button className="btn btn-link" onClick={() => startEdit(customer)}>
                      Edit
                    </button>
                  )}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </div>
  )
}
