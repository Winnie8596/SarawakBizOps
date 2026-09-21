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
