import { useEffect, useState } from 'react'
import { NavLink, Outlet, useLocation, useNavigate } from 'react-router-dom'
import { useAuth } from '../auth/AuthContext'
import { RoleBadge } from '../components/RoleBadge'
import { INVENTORY_ROLES, OFFICE_ROLES } from '../auth/roles'

// `roles` omitted = every signed-in user. This only hides links; the API is what
// actually enforces access, and routes are guarded with the same lists in App.tsx.
const navItems: { to: string; label: string; end: boolean; roles?: readonly string[] }[] = [
  { to: '/', label: 'Dashboard', end: true },
  { to: '/customers', label: 'Customers', end: false, roles: OFFICE_ROLES },
  { to: '/equipment', label: 'Equipment', end: false, roles: OFFICE_ROLES },
  { to: '/service-requests', label: 'Service Requests', end: false, roles: OFFICE_ROLES },
  { to: '/work-orders', label: 'Work Orders', end: false, roles: OFFICE_ROLES },
  { to: '/reports', label: 'Reports', end: false, roles: OFFICE_ROLES },
  { to: '/insights', label: 'AI Insights', end: false, roles: ['Manager'] },
  { to: '/inventory', label: 'Inventory', end: false, roles: INVENTORY_ROLES },
  { to: '/users', label: 'Users', end: false, roles: ['Admin'] }
]

export function AppLayout() {
  const { auth, logout, hasRole } = useAuth()
  const navigate = useNavigate()
  const location = useLocation()
  // Phones only: the links sit behind a menu button (CSS shows them always on wider screens).
  const [menuOpen, setMenuOpen] = useState(false)

  // Opening a page closes the menu, whether by a link or by the browser's back button.
  useEffect(() => {
    setMenuOpen(false)
  }, [location.pathname])

  function handleLogout() {
    logout()
    navigate('/login', { replace: true })
  }

  const visibleItems = navItems.filter(item => !item.roles || hasRole(...item.roles))

  return (
    <div className="app-shell">
      <aside className="sidebar">
        <div className="sidebar-header">
          <div className="sidebar-brand">SarawakBizOps</div>
          <button
            type="button"
            className="sidebar-toggle"
            aria-expanded={menuOpen}
            aria-controls="sidebar-nav"
            aria-label={menuOpen ? 'Close menu' : 'Open menu'}
            onClick={() => setMenuOpen(open => !open)}
          >
            <span aria-hidden="true">{menuOpen ? '✕' : '☰'}</span>
          </button>
        </div>
        <nav id="sidebar-nav" className={`sidebar-nav${menuOpen ? ' is-open' : ''}`} aria-label="Main">
          {visibleItems.map(item => (
            <NavLink
              key={item.to}
              to={item.to}
              end={item.end}
              className={({ isActive }) => `sidebar-link${isActive ? ' is-active' : ''}`}
            >
              {item.label}
            </NavLink>
          ))}
        </nav>
      </aside>

      <div className="app-main">
        <header className="topbar">
          <div className="topbar-title">Field Service Operations</div>
          <div className="topbar-user">
            {auth && (
              <>
                <span className="topbar-name">{auth.fullName}</span>
                <RoleBadge roles={auth.roles} />
              </>
            )}
            <NavLink to="/account/password" className="btn btn-ghost">Change password</NavLink>
            <button className="btn btn-ghost" onClick={handleLogout}>Log out</button>
          </div>
        </header>

        <main className="page">
          <Outlet />
        </main>
      </div>
    </div>
  )
}
