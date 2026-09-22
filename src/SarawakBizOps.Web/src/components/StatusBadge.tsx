interface StatusBadgeProps {
  status: string
}

const STATUS_TONE: Record<string, string> = {
  // Equipment
  Active: 'tone-green',
  Inactive: 'tone-grey',
  UnderMaintenance: 'tone-amber',
  Retired: 'tone-grey',
  // Service requests
  New: 'tone-amber',
  Approved: 'tone-green',
  Assigned: 'tone-green',
  Rejected: 'tone-red',
  Cancelled: 'tone-grey',
  // Work orders
  InProgress: 'tone-steel',
  PendingParts: 'tone-amber',
  Completed: 'tone-green'
}

export function StatusBadge({ status }: StatusBadgeProps) {
  const tone = STATUS_TONE[status] ?? 'tone-grey'
  return <span className={`status-badge ${tone}`}>{formatStatus(status)}</span>
}

// "UnderMaintenance" -> "Under Maintenance"
function formatStatus(status: string): string {
  return status.replace(/([a-z])([A-Z])/g, '$1 $2')
}
