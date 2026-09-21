interface StatusBadgeProps {
  status: string
}

const STATUS_TONE: Record<string, string> = {
  Active: 'tone-green',
  Inactive: 'tone-grey',
  UnderMaintenance: 'tone-amber',
  Retired: 'tone-grey'
}

export function StatusBadge({ status }: StatusBadgeProps) {
  const tone = STATUS_TONE[status] ?? 'tone-grey'
  return <span className={`status-badge ${tone}`}>{formatStatus(status)}</span>
}

// "UnderMaintenance" -> "Under Maintenance"
function formatStatus(status: string): string {
  return status.replace(/([a-z])([A-Z])/g, '$1 $2')
}
