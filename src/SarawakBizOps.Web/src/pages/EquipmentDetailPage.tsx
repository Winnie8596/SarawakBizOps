import { useEffect, useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import { useAuth } from '../auth/AuthContext'
import { getCustomer } from '../api/customers'
import { getEquipmentById, getEquipmentHistory } from '../api/equipment'
import { ApiError } from '../api/client'
import type { Customer, Equipment, ServiceHistory } from '../types'
import { ServiceHistoryTable } from '../components/ServiceHistoryTable'
import { StatusBadge } from '../components/StatusBadge'

export function EquipmentDetailPage() {
  const { auth } = useAuth()
  const { id } = useParams()
  const equipmentId = Number(id)

  const [equipment, setEquipment] = useState<Equipment | null>(null)
  const [customer, setCustomer] = useState<Customer | null>(null)
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
    getEquipmentById(auth.token, equipmentId)
      .then(async e => {
        const owner = await getCustomer(auth.token, e.customerId)
        if (cancelled) return
        setEquipment(e)
        setCustomer(owner)
      })
      .catch(err => {
        if (!cancelled) setError(err instanceof ApiError ? err.message : 'Could not load this equipment record.')
      })
      .finally(() => {
        if (!cancelled) setLoading(false)
      })

    setHistoryLoading(true)
    setHistoryError(null)
    getEquipmentHistory(auth.token, equipmentId)
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
  }, [auth, equipmentId])

  if (loading) return <p className="page-subtitle">Loading equipment…</p>
  if (error || !equipment) {
    return (
      <div>
        <p className="form-error">{error ?? 'Equipment not found.'}</p>
        <Link to="/equipment">← Back to equipment</Link>
      </div>
    )
  }

  return (
    <div>
      <p className="breadcrumb"><Link to="/equipment">← Equipment</Link></p>
      <h1 className="page-title"><span className="mono">{equipment.serialNumber}</span></h1>

      <dl className="detail-list panel">
        <div><dt>Type</dt><dd>{equipment.equipmentType}</dd></div>
        <div><dt>Status</dt><dd><StatusBadge status={equipment.status} /></dd></div>
        <div>
          <dt>Customer</dt>
          <dd>{customer ? <Link to={`/customers/${customer.id}`}>{customer.companyName}</Link> : '—'}</dd>
        </div>
        <div><dt>Brand / model</dt><dd>{[equipment.brand, equipment.model].filter(Boolean).join(' ') || '—'}</dd></div>
        <div><dt>Location</dt><dd>{equipment.location || '—'}</dd></div>
        <div>
          <dt>Installed</dt>
          <dd>{equipment.installationDate ? equipment.installationDate.slice(0, 10) : '—'}</dd>
        </div>
      </dl>

      <h2 className="section-title">Service history</h2>
      <ServiceHistoryTable history={history} loading={historyLoading} error={historyError} />
    </div>
  )
}
