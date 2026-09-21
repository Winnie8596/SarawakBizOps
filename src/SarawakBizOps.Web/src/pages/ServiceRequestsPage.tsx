import { useEffect, useState, type FormEvent } from 'react'
import { Link } from 'react-router-dom'
import { useAuth } from '../auth/AuthContext'
import { getCustomers } from '../api/customers'
import { getEquipment } from '../api/equipment'
import { createServiceRequest, getServiceRequests } from '../api/serviceRequests'
import { ApiError } from '../api/client'
import {
  REQUEST_PRIORITIES,
  REQUEST_STATUSES,
  type Customer,
  type Equipment,
  type RequestPriority,
  type ServiceRequest,
  type ServiceRequestFilters
} from '../types'
import { EmptyState } from '../components/EmptyState'
import { PriorityBadge } from '../components/PriorityBadge'
import { StatusBadge } from '../components/StatusBadge'
import { formatDateTime } from '../utils/format'

interface FormState {
  customerId: number | ''
  equipmentId: number | ''
  problemDescription: string
  priority: RequestPriority
}

const emptyForm: FormState = { customerId: '', equipmentId: '', problemDescription: '', priority: 'Medium' }

const SUMMARY_LENGTH = 90

function summarise(text: string): string {
  return text.length <= SUMMARY_LENGTH ? text : `${text.slice(0, SUMMARY_LENGTH)}…`
}

export function ServiceRequestsPage() {
  const { auth, hasRole } = useAuth()
  const canCreate = hasRole('Admin', 'ServiceStaff')

  // A Manager opens the page on the New requests: that list is their review queue.
  const [filters, setFilters] = useState<ServiceRequestFilters>({
    status: hasRole('Manager') ? 'New' : '',
    priority: '',
    customerId: ''
  })

  const [requests, setRequests] = useState<ServiceRequest[]>([])
  const [customers, setCustomers] = useState<Customer[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [reloadKey, setReloadKey] = useState(0)

  const [formOpen, setFormOpen] = useState(false)
  const [form, setForm] = useState<FormState>(emptyForm)
  const [equipment, setEquipment] = useState<Equipment[]>([])
  const [equipmentLoading, setEquipmentLoading] = useState(false)
  const [formError, setFormError] = useState<string | null>(null)
  const [saving, setSaving] = useState(false)
  const [notice, setNotice] = useState<{ id: number } | null>(null)

  useEffect(() => {
    if (!auth) return
    let cancelled = false

    getCustomers(auth.token)
      .then(data => {
        if (!cancelled) setCustomers(data)
      })
      .catch(() => {
        if (!cancelled) setError('Could not load customers.')
      })

    return () => {
      cancelled = true
    }
  }, [auth])

  useEffect(() => {
    if (!auth) return
    let cancelled = false

    setLoading(true)
    setError(null)
    getServiceRequests(auth.token, filters)
      .then(data => {
        if (!cancelled) setRequests(data)
      })
      .catch(err => {
        if (!cancelled) setError(err instanceof ApiError ? err.message : 'Could not load service requests.')
      })
      .finally(() => {
        if (!cancelled) setLoading(false)
      })

    return () => {
      cancelled = true
    }
  }, [auth, filters, reloadKey])

  // The equipment picker only offers what the chosen customer owns (BR-10); the API checks it again.
  // Retired equipment is left out because the API would refuse it.
  useEffect(() => {
    if (!auth || form.customerId === '') {
      setEquipment([])
      return
    }
    let cancelled = false

    setEquipmentLoading(true)
    getEquipment(auth.token, form.customerId)
      .then(items => {
        if (!cancelled) setEquipment(items.filter(e => e.status !== 'Retired'))
      })
      .catch(() => {
        if (!cancelled) setFormError('Could not load this customer’s equipment.')
      })
      .finally(() => {
        if (!cancelled) setEquipmentLoading(false)
      })

    return () => {
      cancelled = true
    }
  }, [auth, form.customerId])

  function openForm() {
    setForm(emptyForm)
    setFormError(null)
    setNotice(null)
    setFormOpen(true)
  }

  async function handleSubmit(event: FormEvent) {
    event.preventDefault()
    if (!auth || form.customerId === '' || form.equipmentId === '') return

    setSaving(true)
    setFormError(null)
    try {
      const created = await createServiceRequest(auth.token, {
        customerId: form.customerId,
        equipmentId: form.equipmentId,
        problemDescription: form.problemDescription,
        priority: form.priority
      })
      setFormOpen(false)
      setNotice({ id: created.id })
      setReloadKey(k => k + 1)
    } catch (err) {
      setFormError(err instanceof ApiError ? err.message : 'Could not submit this request.')
    } finally {
      setSaving(false)
    }
  }

  const filtered = Boolean(filters.status || filters.priority || filters.customerId)

  return (
    <div>
      <div className="page-header">
        <div>
          <h1 className="page-title">Service requests</h1>
          <p className="page-subtitle">
            {hasRole('Manager')
              ? 'Review new requests, then approve or reject them.'
              : 'Faults reported by customers, and where each one stands.'}
          </p>
        </div>
        {canCreate && (
          <button className="btn btn-primary" onClick={openForm}>
            New service request
          </button>
        )}
      </div>

      {notice && (
        <p className="form-notice" role="status">
          Request #{notice.id} submitted. <Link to={`/service-requests/${notice.id}`}>View it</Link>
        </p>
      )}

      {formOpen && (
        <form className="panel form-panel" onSubmit={handleSubmit}>
          <h2 className="panel-title">New service request</h2>

          <div className="field-grid">
            <label className="field">
              <span className="field-label">Customer</span>
              <select
                value={form.customerId}
                onChange={e => setForm({
                  ...form,
                  customerId: e.target.value ? Number(e.target.value) : '',
                  equipmentId: ''
                })}
                required
              >
                <option value="">Select a customer…</option>
                {customers.map(c => (
                  <option key={c.id} value={c.id}>{c.companyName}</option>
                ))}
              </select>
            </label>

            <div className="field">
              <label className="field-label" htmlFor="request-equipment">Equipment</label>
              <select
                id="request-equipment"
                aria-describedby="request-equipment-hint"
                value={form.equipmentId}
                onChange={e => setForm({ ...form, equipmentId: e.target.value ? Number(e.target.value) : '' })}
                disabled={form.customerId === '' || equipmentLoading}
                required
              >
                <option value="">
                  {form.customerId === '' ? 'Choose a customer first' : equipmentLoading ? 'Loading…' : 'Select equipment…'}
                </option>
                {equipment.map(e => (
                  <option key={e.id} value={e.id}>{e.serialNumber} — {e.equipmentType}</option>
                ))}
              </select>
              {form.customerId !== '' && !equipmentLoading && equipment.length === 0 && (
                <span id="request-equipment-hint" className="form-hint">
                  This customer has no equipment that can be serviced.
                </span>
              )}
            </div>

            <label className="field">
              <span className="field-label">Priority</span>
              <select
                value={form.priority}
                onChange={e => setForm({ ...form, priority: e.target.value as RequestPriority })}
              >
                {REQUEST_PRIORITIES.map(p => (
                  <option key={p} value={p}>{p}</option>
                ))}
              </select>
            </label>

            <label className="field field-wide">
              <span className="field-label">What is wrong?</span>
              <textarea
                value={form.problemDescription}
                onChange={e => setForm({ ...form, problemDescription: e.target.value })}
                maxLength={2000}
                rows={4}
                required
              />
            </label>
          </div>

          {formError && <p className="form-error">{formError}</p>}

          <div className="panel-actions">
            <button type="button" className="btn btn-ghost" onClick={() => setFormOpen(false)}>
              Cancel
            </button>
            <button
              type="submit"
              className="btn btn-primary"
              disabled={saving || form.equipmentId === '' || form.problemDescription.trim() === ''}
            >
              {saving ? 'Submitting…' : 'Submit request'}
            </button>
          </div>
        </form>
      )}

      <div className="toolbar">
        <label className="field field-inline">
          <span className="field-label">Status</span>
          <select
            value={filters.status ?? ''}
            onChange={e => setFilters({ ...filters, status: e.target.value as ServiceRequestFilters['status'] })}
          >
            <option value="">All statuses</option>
            {REQUEST_STATUSES.map(s => (
              <option key={s} value={s}>{s}</option>
            ))}
          </select>
        </label>

        <label className="field field-inline">
          <span className="field-label">Priority</span>
          <select
            value={filters.priority ?? ''}
            onChange={e => setFilters({ ...filters, priority: e.target.value as ServiceRequestFilters['priority'] })}
          >
            <option value="">All priorities</option>
            {REQUEST_PRIORITIES.map(p => (
              <option key={p} value={p}>{p}</option>
            ))}
          </select>
        </label>

        <label className="field field-inline">
          <span className="field-label">Customer</span>
          <select
            value={filters.customerId ?? ''}
            onChange={e => setFilters({ ...filters, customerId: e.target.value ? Number(e.target.value) : '' })}
          >
            <option value="">All customers</option>
            {customers.map(c => (
              <option key={c.id} value={c.id}>{c.companyName}</option>
            ))}
          </select>
        </label>
      </div>

      {error && <p className="form-error">{error}</p>}

      {loading ? (
        <p className="page-subtitle">Loading service requests…</p>
      ) : requests.length === 0 ? (
        <EmptyState
          title={filtered ? 'No requests match these filters' : 'No service requests yet'}
          description={filtered
            ? 'Try a different status, priority or customer.'
            : canCreate
              ? 'Raise the first request when a customer reports a fault.'
              : 'Service Staff raise requests when a customer reports a fault.'}
        />
      ) : (
        <table className="data-table">
          <thead>
            <tr>
              <th>#</th>
              <th>Raised (MYT)</th>
              <th>Customer</th>
              <th>Equipment</th>
              <th>Problem</th>
              <th>Priority</th>
              <th>Status</th>
            </tr>
          </thead>
          <tbody>
            {requests.map(r => (
              <tr key={r.id}>
                <td><Link to={`/service-requests/${r.id}`}>#{r.id}</Link></td>
                <td className="mono">{formatDateTime(r.createdAt)}</td>
                <td>{r.customerName}</td>
                <td><span className="mono">{r.equipmentSerialNumber}</span> · {r.equipmentType}</td>
                <td>{summarise(r.problemDescription)}</td>
                <td><PriorityBadge priority={r.priority} /></td>
                <td><StatusBadge status={r.status} /></td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </div>
  )
}
