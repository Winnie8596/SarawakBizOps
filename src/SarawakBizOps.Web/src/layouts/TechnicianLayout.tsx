import { Link, Outlet, useNavigate } from 'react-router-dom'
import { useAuth } from '../auth/AuthContext'

// Deliberately not AppLayout: no sidebar, a single scrolling column and a sticky bottom action bar,
// designed for a 360 px phone screen (NFR-04) rather than the ≥1024 px office console.
export function TechnicianLayout() {
  const { auth, logout } = useAuth()
  const navigate = useNavigate()

  function handleLogout() {
    logout()
    navigate('/login', { replace: true })
  }

  return (
    <div className="tech-shell">
      <header className="tech-topbar">
        <Link to="/tech/jobs" className="tech-brand">My Jobs</Link>
        <div className="tech-topbar-user">
          {auth && <span className="tech-name">{auth.fullName.split(' ')[0]}</span>}
          <button className="btn btn-ghost" onClick={handleLogout}>Log out</button>
        </div>
      </header>

      <main className="tech-page">
        <Outlet />
      </main>
    </div>
  )
}
