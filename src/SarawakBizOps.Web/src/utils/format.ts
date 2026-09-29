// The API stores UTC (PRD Q-02) and the UI shows Malaysia time (UTC+8).
// EF Core hands back DateTime values with no "Z", which browsers would parse as
// *local* time, so a missing offset is treated as UTC before formatting.
const HAS_OFFSET = /(Z|[+-]\d{2}:?\d{2})$/i

export function formatDateTime(utcIso: string): string {
  const date = new Date(HAS_OFFSET.test(utcIso) ? utcIso : `${utcIso}Z`)
  return date.toLocaleString('en-MY', {
    timeZone: 'Asia/Kuching',
    day: '2-digit',
    month: 'short',
    year: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
    hour12: false
  })
}

// Money is shown as RM with two decimals and thousands separators, e.g. "RM 1,234.50".
export function formatMoney(amount: number): string {
  return `RM ${amount.toLocaleString('en-MY', { minimumFractionDigits: 2, maximumFractionDigits: 2 })}`
}

// Stock quantities carry up to three decimals (litres, metres); whole numbers are shown without any.
export function formatQuantity(quantity: number): string {
  return quantity.toLocaleString('en-MY', { maximumFractionDigits: 3 })
}

/**
 * The UTC instant at which a Malaysia-time (UTC+8, no DST) calendar day starts, optionally shifted by
 * whole days: mytDayStartUtc('2026-09-28') === '2026-09-27T16:00:00.000Z'.
 */
export function mytDayStartUtc(isoDate: string, addDays = 0): string {
  const [year, month, day] = isoDate.split('-').map(Number)
  return new Date(Date.UTC(year, month - 1, day + addDays) - 8 * 60 * 60 * 1000).toISOString()
}

// File sizes as people read them: "812 B", "24 KB", "1.2 MB".
export function formatFileSize(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`
  if (bytes < 1024 * 1024) return `${Math.round(bytes / 1024)} KB`
  return `${(bytes / (1024 * 1024)).toLocaleString('en-MY', { maximumFractionDigits: 1 })} MB`
}

// A 'yyyy-MM' month key (already in Malaysia time) as people read it: "Sep 2026", or just "Sep" when short.
// A fixed table rather than toLocaleString: ICU versions disagree on short month names ("Sep" vs "Sept").
const MONTHS = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec']

export function formatMonth(monthKey: string, style: 'long' | 'short' = 'long'): string {
  const [year, month] = monthKey.split('-').map(Number)
  return style === 'long' ? `${MONTHS[month - 1]} ${year}` : MONTHS[month - 1]
}

// Phase 12: an average duration in hours as people read it — minutes under an hour, hours under two days, then
// days: "45 min", "22.8 h", "3.2 days". Null (nothing measured) is a dash.
export function formatHours(hours: number | null): string {
  if (hours === null) return '—'
  if (hours < 1) return `${Math.round(hours * 60)} min`
  if (hours < 48) return `${hours.toLocaleString('en-MY', { maximumFractionDigits: 1 })} h`
  return `${(hours / 24).toLocaleString('en-MY', { maximumFractionDigits: 1 })} days`
}

// Two 'yyyy-MM' month keys as a period: "Apr–Sep 2026", or "Nov 2026–Jan 2027" across a year.
export function formatPeriod(fromMonth: string, toMonth: string): string {
  if (fromMonth.slice(0, 4) === toMonth.slice(0, 4)) {
    return `${formatMonth(fromMonth, 'short')}–${formatMonth(toMonth)}`
  }
  return `${formatMonth(fromMonth)}–${formatMonth(toMonth)}`
}
