import { useEffect, useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import { useAuth } from '../auth/AuthContext'
import { getCustomer, getCustomerHistory } from '../api/customers'
import { getEquipment } from '../api/equipment'
import { ApiError } from '../api/client'
import type { Customer, Equipment, ServiceHistory } from '../types'
import { EmptyState } from '../components/EmptyState'
import { ServiceHistoryTable } from '../components/ServiceHistoryTable'
import { StatusBadge } from '../components/StatusBadge'

export function CustomerDetailPage() {
  const { auth } = useAuth()
  const { id } = useParams()
  const customerId = Number(id)

  const [customer, setCustomer] = useState<Customer | null>(null)
  const [equipment, setEquipment] = useState<Equipment[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  const [history, setHistory] = useState<ServiceHistory | null>(null)
  const [historyLoading, setHistoryLoading] = useState(true)
  const [historyError, setHistoryError] = useState<string | null>(null)

  useEffect(() => {
    if (!auth) return
    let cancelled = false

    setLoading(true)
    setError(null)
    Promise.all([getCustomer(auth.token, customerId), getEquipment(auth.token, customerId)])
      .then(([c, e]) => {
        if (cancelled) return
        setCustomer(c)
        setEquipment(e)
      })
      .catch(err => {
        if (!cancelled) setError(err instanceof ApiError ? err.message : 'Could not load this customer.')
      })
      .finally(() => {
        if (!cancelled) setLoading(false)
      })

    setHistoryLoading(true)
    setHistoryError(null)
    getCustomerHistory(auth.token, customerId)
      .then(h => {
        if (!cancelled) setHistory(h)
      })
      .catch(err => {
        if (!cancelled) setHistoryError(err instanceof ApiError ? err.message : 'Could not load service history.')
      })
      .finally(() => {
        if (!cancelled) setHistoryLoading(false)
      })

    return () => {
      cancelled = true
    }
  }, [auth, customerId])

  if (loading) return <p className="page-subtitle">Loading customer…</p>
  if (error || !customer) {
    return (
      <div>
        <p className="form-error">{error ?? 'Customer not found.'}</p>
        <Link to="/customers">← Back to customers</Link>
      </div>
    )
  }

  return (
    <div>
      <p className="breadcrumb"><Link to="/customers">← Customers</Link></p>
      <h1 className="page-title">{customer.companyName}</h1>

      <dl className="detail-list panel">
        <div><dt>Contact</dt><dd>{customer.contactPerson || '—'}</dd></div>
        <div><dt>Phone</dt><dd className="mono">{customer.phone || '—'}</dd></div>
        <div><dt>Email</dt><dd>{customer.email || '—'}</dd></div>
        <div><dt>Address</dt><dd>{customer.address || '—'}</dd></div>
      </dl>

      <h2 className="section-title">Equipment</h2>
      {equipment.length === 0 ? (
        <EmptyState title="No equipment registered for this customer" />
      ) : (
        <table className="data-table">
          <thead>
            <tr>
              <th>Serial number</th>
              <th>Type</th>
              <th>Status</th>
              <th>Location</th>
            </tr>
          </thead>
          <tbody>
            {equipment.map(item => (
              <tr key={item.id}>
                <td className="mono"><Link to={`/equipment/${item.id}`}>{item.serialNumber}</Link></td>
                <td>{item.equipmentType}{item.brand ? ` · ${item.brand}` : ''}</td>
                <td><StatusBadge status={item.status} /></td>
                <td>{item.location || '—'}</td>
              </tr>
            ))}
          </tbody>
        </table>
      )}

      <h2 className="section-title">Service history</h2>
      <ServiceHistoryTable history={history} loading={historyLoading} error={historyError} />
    </div>
  )
}
