import { Link } from 'react-router-dom'

export function NotFoundPage() {
  return (
    <div className="auth-screen">
      <div className="auth-card" style={{ textAlign: 'center' }}>
        <h1 className="auth-brand">Page not found</h1>
        <p className="auth-subtitle">That page doesn't exist.</p>
        <Link className="btn btn-primary" to="/">
          Back to dashboard
        </Link>
      </div>
    </div>
  )
}
