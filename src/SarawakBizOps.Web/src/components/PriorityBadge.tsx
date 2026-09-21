const PRIORITY_TONE: Record<string, string> = {
  Urgent: 'tone-red',
  High: 'tone-amber',
  Medium: 'tone-steel',
  Low: 'tone-grey'
}

export function PriorityBadge({ priority }: { priority: string }) {
  return <span className={`status-badge ${PRIORITY_TONE[priority] ?? 'tone-grey'}`}>{priority}</span>
}
