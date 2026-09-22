import { formatDateTime } from '../utils/format'

export interface TimelineEntry {
  label: string
  at?: string | null
  by?: string | null
  detail?: string | null
}

/**
 * Derived purely from the timestamp/actor columns already on the record (no separate history table —
 * D-05's "no AuditLog" decision extends to the timeline too). Entries without a timestamp haven't
 * happened yet and are left out, so the list always reads as "what happened, in order, so far".
 */
export function StatusTimeline({ entries }: { entries: TimelineEntry[] }) {
  const shown = entries.filter(e => e.at)
  if (shown.length === 0) return null

  return (
    <div className="panel">
      <h2 className="panel-title">Timeline</h2>
      <dl className="detail-list">
        {shown.map(e => (
          <div key={e.label}>
            <dt>{e.label}</dt>
            <dd>
              {formatDateTime(e.at!)}
              {e.by ? ` by ${e.by}` : ''}
              {e.detail ? <p className="prose">{e.detail}</p> : null}
            </dd>
          </div>
        ))}
      </dl>
    </div>
  )
}
