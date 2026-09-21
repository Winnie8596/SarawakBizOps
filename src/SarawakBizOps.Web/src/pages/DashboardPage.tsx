import { useEffect, useState } from 'react'
import { useAuth } from '../auth/AuthContext'
import { getCustomers } from '../api/customers'
import { getEquipment } from '../api/equipment'

export function DashboardPage() {
  const { auth } = useAuth()
  const [customerCount, setCustomerCount] = useState<number | null>(null)
  const [equipmentCount, setEquipmentCount] = useState<number | null>(null)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    if (!auth) return
    let cancelled = false

    Promise.all([getCustomers(auth.token), getEquipment(auth.token)])
      .then(([customers, equipment]) => {
        if (cancelled) return
        setCustomerCount(customers.length)
        setEquipmentCount(equipment.length)
      })
      .catch(() => {
        if (!cancelled) setError('Could not load the latest counts.')
      })

    return () => {
      cancelled = true
    }
  }, [auth])

  return (
    <div>
      <h1 className="page-title">Welcome back, {auth?.fullName.split(' ')[0]}</h1>
      <p className="page-subtitle">
        This dashboard grows into full operational analytics (Section 12 of the design
        doc, <code className="mono">/api/dashboard/summary</code>) once service requests and
        work orders are built. For now, here's what the system already tracks.
      </p>

      {error && <p className="form-error">{error}</p>}

      <div className="stat-grid">
        <div className="stat-card">
          <span className="stat-value">{customerCount ?? '—'}</span>
          <span className="stat-label">Customers</span>
        </div>
        <div className="stat-card">
          <span className="stat-value">{equipmentCount ?? '—'}</span>
          <span className="stat-label">Equipment records</span>
        </div>
      </div>
    </div>
  )
}
