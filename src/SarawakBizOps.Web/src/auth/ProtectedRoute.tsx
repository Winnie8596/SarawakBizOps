import { Navigate, Outlet, useLocation } from 'react-router-dom'
import { useAuth } from './AuthContext'

interface ProtectedRouteProps {
  roles?: string[]
}

export function ProtectedRoute({ roles }: ProtectedRouteProps) {
  const { isAuthenticated, restoring, hasRole } = useAuth()
  const location = useLocation()

  // On first load the session (if any) is still being restored from the cookie: don't send anyone to /login yet.
  if (restoring) {
    return (
      <p className="page-subtitle" role="status">
        Loading…
      </p>
    )
  }

  if (!isAuthenticated) {
    return <Navigate to="/login" replace state={{ from: location.pathname }} />
  }

  if (roles && roles.length > 0 && !hasRole(...roles)) {
    return <Navigate to="/" replace />
  }

  return <Outlet />
}
