import type { ReactNode } from 'react'
import type { CountByKey, MonthCount } from '../types'
import { formatMonth } from '../utils/format'

// Phase 11 (Open Question 6 → hand-rolled, PRD D-06): the dashboard's charts are plain HTML and CSS, no chart
// library. Each one is real, readable markup — a table, an ordered list — so the numbers are there for a screen
// reader and for a test; the bars themselves are decoration (aria-hidden) sized as a share of the largest value.

function share(value: number, max: number): string {
  return max > 0 ? `${(value / max) * 100}%` : '0%'
}

interface BarTableProps {
  caption: string
  rows: CountByKey[]
  label?: (key: string) => ReactNode
  /** Shown instead of the bars when every count is zero. */
  empty: string
}

/** Horizontal bars, one per category, with the count printed beside each. */
export function BarTable({ caption, rows, label = key => key, empty }: BarTableProps) {
  const max = Math.max(0, ...rows.map(r => r.count))

  return (
    <table className="bar-table">
      <caption className="visually-hidden">{caption}</caption>
      <tbody>
        {max === 0 ? (
          <tr>
            <td className="bar-table-empty">{empty}</td>
          </tr>
        ) : (
          rows.map(row => (
            <tr key={row.key}>
              <th scope="row">{label(row.key)}</th>
              <td className="bar-cell">
                <span className="bar-track" aria-hidden="true">
                  <span className="bar-fill" style={{ width: share(row.count, max) }} />
                </span>
                <span className="bar-value">{row.count}</span>
              </td>
            </tr>
          ))
        )}
      </tbody>
    </table>
  )
}

interface MonthColumnsProps {
  label: string
  months: MonthCount[]
  unit: (count: number) => string
  empty: string
}

/** Vertical columns, oldest month first; the last (current) month is highlighted. */
export function MonthColumns({ label, months, unit, empty }: MonthColumnsProps) {
  const max = Math.max(0, ...months.map(m => m.count))
  if (max === 0) return <p className="chart-empty">{empty}</p>

  return (
    <ol className="column-chart" aria-label={label}>
      {months.map((m, i) => (
        <li key={m.month} className={i === months.length - 1 ? 'is-current' : undefined}>
          <span className="visually-hidden">{formatMonth(m.month)}: {unit(m.count)}</span>
          <span className="column-value" aria-hidden="true">{m.count}</span>
          <span className="column-track" aria-hidden="true">
            <span className="column-fill" style={{ height: share(m.count, max) }} />
          </span>
          <span className="column-label" aria-hidden="true">{formatMonth(m.month, 'short')}</span>
        </li>
      ))}
    </ol>
  )
}
