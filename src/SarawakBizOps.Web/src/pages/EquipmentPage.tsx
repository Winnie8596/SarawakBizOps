import { useEffect, useState, type FormEvent } from 'react'
import { useAuth } from '../auth/AuthContext'
import { getCustomers } from '../api/customers'
import { createEquipment, getEquipment } from '../api/equipment'
import { ApiError } from '../api/client'
import type { Customer, Equipment, EquipmentInput } from '../types'
import { EmptyState } from '../components/EmptyState'
import { StatusBadge } from '../components/StatusBadge'

const emptyForm: EquipmentInput = {
  customerId: 0,
  serialNumber: '',
  equipmentType: '',
  brand: '',
  model: '',
  location: ''
}

export function EquipmentPage() {
  const { auth, hasRole } = useAuth()
  const canEdit = hasRole('Admin', 'ServiceStaff')

  const [customers, setCustomers] = useState<Customer[]>([])
  const [equipment, setEquipment] = useState<Equipment[]>([])
  const [filterCustomerId, setFilterCustomerId] = useState<number | ''>('')
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  const [creating, setCreating] = useState(false)
  const [form, setForm] = useState<EquipmentInput>(emptyForm)
  const [formError, setFormError] = useState<string | null>(null)
  const [saving, setSaving] = useState(false)

  useEffect(() => {
    if (!auth) return
    getCustomers(auth.token).then(setCustomers).catch(() => undefined)
  }, [auth])

  useEffect(() => {
    if (!auth) return
    loadEquipment()
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [auth, filterCustomerId])

  async function loadEquipment() {
    if (!auth) return
    setLoading(true)
    setError(null)
    try {
      const data = await getEquipment(auth.token, filterCustomerId || undefined)
      setEquipment(data)
    } catch {
      setError('Could not load equipment.')
    } finally {
      setLoading(false)
    }
  }

  function startCreate() {
    setForm({ ...emptyForm, customerId: customers[0]?.id ?? 0 })
    setFormError(null)
    setCreating(true)
  }

  async function handleSubmit(event: FormEvent) {
    event.preventDefault()
    if (!auth) return

    setSaving(true)
    setFormError(null)

    try {
      await createEquipment(auth.token, form)
      setCreating(false)
      await loadEquipment()
    } catch (err) {
      setFormError(err instanceof ApiError ? err.message : 'Could not save this equipment record.')
    } finally {
      setSaving(false)
    }
  }

  function customerName(customerId: number): string {
    return customers.find(c => c.id === customerId)?.companyName ?? `#${customerId}`
  }

  return (
    <div>
      <div className="page-header">
        <div>
          <h1 className="page-title">Equipment</h1>
          <p className="page-subtitle">Machines installed at customer sites.</p>
        </div>
        {canEdit && (
          <button className="btn btn-primary" onClick={startCreate} disabled={customers.length === 0}>
            New equipment
          </button>
        )}
      </div>

      <div className="toolbar">
        <label className="field field-inline">
          <span className="field-label">Filter by customer</span>
          <select
            value={filterCustomerId}
            onChange={e => setFilterCustomerId(e.target.value ? Number(e.target.value) : '')}
          >
            <option value="">All customers</option>
            {customers.map(c => (
              <option key={c.id} value={c.id}>{c.companyName}</option>
            ))}
          </select>
        </label>
      </div>

      {error && <p className="form-error">{error}</p>}

      {creating && (
        <form className="panel form-panel" onSubmit={handleSubmit}>
          <h2 className="panel-title">New equipment</h2>

          <div className="field-grid">
            <label className="field">
              <span className="field-label">Customer</span>
              <select
                value={form.customerId}
                onChange={e => setForm({ ...form, customerId: Number(e.target.value) })}
                required
              >
                {customers.map(c => (
                  <option key={c.id} value={c.id}>{c.companyName}</option>
                ))}
              </select>
            </label>

            <label className="field">
              <span className="field-label">Serial number</span>
              <input
                className="mono"
                value={form.serialNumber}
                onChange={e => setForm({ ...form, serialNumber: e.target.value })}
                required
              />
            </label>

            <label className="field">
              <span className="field-label">Equipment type</span>
              <input
                value={form.equipmentType}
                onChange={e => setForm({ ...form, equipmentType: e.target.value })}
                placeholder="Pump, Generator, Compressor…"
                required
              />
            </label>

            <label className="field">
              <span className="field-label">Brand</span>
              <input
                value={form.brand}
                onChange={e => setForm({ ...form, brand: e.target.value })}
              />
            </label>

            <label className="field">
              <span className="field-label">Model</span>
              <input
                value={form.model}
                onChange={e => setForm({ ...form, model: e.target.value })}
              />
            </label>

            <label className="field">
              <span className="field-label">Location</span>
              <input
                value={form.location}
                onChange={e => setForm({ ...form, location: e.target.value })}
                placeholder="Site or building"
              />
            </label>
          </div>

          {formError && <p className="form-error">{formError}</p>}

          <div className="panel-actions">
            <button type="button" className="btn btn-ghost" onClick={() => setCreating(false)}>
              Cancel
            </button>
            <button type="submit" className="btn btn-primary" disabled={saving}>
              {saving ? 'Saving…' : 'Save equipment'}
            </button>
          </div>
        </form>
      )}

      {loading ? (
        <p className="page-subtitle">Loading equipment…</p>
      ) : equipment.length === 0 ? (
        <EmptyState
          title="No equipment records"
          description={canEdit
            ? 'Register the first piece of equipment for a customer.'
            : 'Ask a Service Staff member or Admin to register one.'}
        />
      ) : (
        <table className="data-table">
          <thead>
            <tr>
              <th>Serial number</th>
              <th>Type</th>
              <th>Customer</th>
              <th>Status</th>
              <th>Location</th>
            </tr>
          </thead>
          <tbody>
            {equipment.map(item => (
              <tr key={item.id}>
                <td className="mono">{item.serialNumber}</td>
                <td>{item.equipmentType}{item.brand ? ` · ${item.brand}` : ''}</td>
                <td>{customerName(item.customerId)}</td>
                <td><StatusBadge status={item.status} /></td>
                <td>{item.location || '—'}</td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </div>
  )
}
