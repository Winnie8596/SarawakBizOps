import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { useAuth } from '../../auth/AuthContext'
import { getMyWorkOrders } from '../../api/workOrders'
import { ApiError } from '../../api/client'
import type { WorkOrder } from '../../types'
import { EmptyState } from '../../components/EmptyState'
import { PriorityBadge } from '../../components/PriorityBadge'
import { StatusBadge } from '../../components/StatusBadge'
import { formatDateTime } from '../../utils/format'

export function TechnicianJobsPage() {
  const { auth } = useAuth()
  const [jobs, setJobs] = useState<WorkOrder[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    if (!auth) return
    let cancelled = false

    setLoading(true)
    setError(null)
    getMyWorkOrders()
      .then(data => {
        if (!cancelled) setJobs(data)
      })
      .catch(err => {
        if (!cancelled) setError(err instanceof ApiError ? err.message : 'Could not load your jobs.')
      })
      .finally(() => {
        if (!cancelled) setLoading(false)
      })

    return () => {
      cancelled = true
    }
  }, [auth])

  return (
    <div>
      <h1 className="page-title">My Jobs</h1>
      {loading ? (
        <p className="page-subtitle">Loading your jobs…</p>
      ) : error ? (
        <p className="form-error">{error}</p>
      ) : jobs.length === 0 ? (
        <EmptyState
          title="No jobs assigned yet"
          description="A Manager will assign you a job once a service request is approved."
        />
      ) : (
        <div className="tech-job-list">
          {jobs.map(job => (
            <Link key={job.id} to={`/tech/jobs/${job.id}`} className="tech-job-card">
              <div className="tech-job-card-header">
                <span className="mono">{job.equipmentSerialNumber}</span>
                <StatusBadge status={job.status} />
              </div>
              <p className="tech-job-customer">{job.customerName}</p>
              <p className="tech-job-problem">{job.problemDescription}</p>
              <div className="tech-job-card-footer">
                <PriorityBadge priority={job.priority} />
                {job.assignedAt && <span className="tech-job-date">{formatDateTime(job.assignedAt)}</span>}
              </div>
            </Link>
          ))}
        </div>
      )}
    </div>
  )
}
