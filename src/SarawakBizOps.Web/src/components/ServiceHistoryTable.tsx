import { Link } from 'react-router-dom'
import type { ServiceHistory } from '../types'
import { formatDateTime } from '../utils/format'
import { EmptyState } from './EmptyState'
import { StatusBadge } from './StatusBadge'

interface ServiceHistoryTableProps {
  history: ServiceHistory | null
  loading: boolean
  error: string | null
}

export function ServiceHistoryTable({ history, loading, error }: ServiceHistoryTableProps) {
  if (loading) return <p className="page-subtitle">Loading service history…</p>
  if (error) return <p className="form-error">{error}</p>

  if (!history || history.items.length === 0) {
    return (
      <EmptyState
        title="No service history yet"
        description="Service requests and work orders will appear here once they exist."
      />
    )
  }

  return (
    <table className="data-table">
      <thead>
        <tr>
          <th>Date (MYT)</th>
          <th>Type</th>
          <th>Status</th>
          <th>Priority</th>
          <th>Summary</th>
        </tr>
      </thead>
      <tbody>
        {history.items.map(item => (
          <tr key={`${item.type}-${item.id}`}>
            <td className="mono">{formatDateTime(item.occurredAtUtc)}</td>
            <td>
              {item.type === 'ServiceRequest'
                ? <Link to={`/service-requests/${item.id}`}>Service Request #{item.id}</Link>
                : <>{item.type.replace(/([a-z])([A-Z])/g, '$1 $2')} #{item.id}</>}
            </td>
            <td><StatusBadge status={item.status} /></td>
            <td>{item.priority}</td>
            <td>{item.summary}</td>
          </tr>
        ))}
      </tbody>
    </table>
  )
}
