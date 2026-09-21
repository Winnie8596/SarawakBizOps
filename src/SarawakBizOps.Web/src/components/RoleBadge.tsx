interface RoleBadgeProps {
  roles: string[]
}

export function RoleBadge({ roles }: RoleBadgeProps) {
  if (roles.length === 0) return null
  return <span className="role-badge">{roles[0]}</span>
}
