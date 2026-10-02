import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { useAuth } from '../auth/AuthContext'
import { getReports } from '../api/reports'
import { ApiError } from '../api/client'
import type { ServiceReport } from '../types'
import { EmptyState } from '../components/EmptyState'
import { StatusBadge } from '../components/StatusBadge'
import { useReportPdf } from '../hooks/useReportPdf'
import { formatDateTime } from '../utils/format'

// PRD §6.7: every generated service report, newest first. Reports are generated from the work-order page.
export function ReportsPage() {
  const { auth } = useAuth()
  const [reports, setReports] = useState<ServiceReport[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    if (!auth) return
    let cancelled = false

    setLoading(true)
    setError(null)
    getReports(auth.token)
      .then(data => {
        if (!cancelled) setReports(data)
      })
      .catch(err => {
        if (!cancelled) setError(err instanceof ApiError ? err.message : 'Could not load the reports.')
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
      <div className="page-header">
        <div>
          <h1 className="page-title">Service reports</h1>
          <p className="page-subtitle">
            The PDF report of each completed job. Reports are locked once the job is approved.
          </p>
        </div>
      </div>

      {error && <p className="form-error">{error}</p>}

      {loading ? (
        <p className="page-subtitle">Loading reports…</p>
      ) : error ? null : reports.length === 0 ? (
        <EmptyState
          title="No reports yet"
          description="A Manager generates a job’s report from its work-order page once the technician has completed it."
        />
      ) : (
        <div className="table-scroll">
          <table className="data-table">
            <thead>
              <tr>
                <th>Report no.</th>
                <th>Work order</th>
                <th>Customer</th>
                <th>Equipment</th>
                <th>Technician</th>
                <th>Generated (MYT)</th>
                <th>State</th>
                <th><span className="visually-hidden">Actions</span></th>
              </tr>
            </thead>
            <tbody>
              {reports.map(report => <ReportRow key={report.id} report={report} />)}
            </tbody>
          </table>
        </div>
      )}
    </div>
  )
}

function ReportRow({ report }: { report: ServiceReport }) {
  const pdf = useReportPdf(report.workOrderId, report.reportNumber)

  return (
    <tr>
      <td>
        <span className="mono">{report.reportNumber}</span>
        {report.revision > 1 && <span className="form-hint"> · rev {report.revision}</span>}
      </td>
      <td><Link to={`/work-orders/${report.workOrderId}`}>#{report.workOrderId}</Link></td>
      <td>{report.customerName}</td>
      <td><span className="mono">{report.equipmentSerialNumber}</span> · {report.equipmentType}</td>
      <td>{report.technicianName}</td>
      <td className="mono">{formatDateTime(report.generatedAt)}<br /><span className="form-hint">{report.generatedByName}</span></td>
      <td>
        {report.isLocked
          ? <span className="status-badge tone-green">Locked</span>
          : <StatusBadge status={report.workOrderStatus} />}
      </td>
      <td>
        <button
          className="btn btn-ghost"
          onClick={pdf.download}
          disabled={pdf.fetching}
          aria-label={`Download ${report.reportNumber}`}
        >
          {pdf.fetching ? 'Loading…' : 'Download'}
        </button>
        {pdf.error && <p className="form-error">{pdf.error}</p>}
      </td>
    </tr>
  )
}
