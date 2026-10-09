import { Navigate, NavLink, Outlet, useLocation } from 'react-router'
import { useAuth } from '@/auth/AuthContext'
import { Button, Loading } from '@/components/ui'

const nav = [
  { to: '/admin', label: 'Gösterge paneli', end: true },
  { to: '/admin/bookings', label: 'Rezervasyonlar' },
  { to: '/admin/calendar', label: 'Fiyat & müsaitlik' },
  { to: '/admin/rooms', label: 'Odalar' },
  { to: '/admin/room-types', label: 'Oda tipleri & planlar' },
  { to: '/admin/channels', label: 'Kanallar (OTA)' },
  { to: '/admin/users', label: 'Kullanıcılar', role: 'Admin' },
]

export default function AdminLayout() {
  const { isAuthenticated, isLoading, user, logout, hasRole } = useAuth()
  const location = useLocation()

  if (!isAuthenticated) return <Navigate to="/admin/login" replace state={{ from: location.pathname }} />
  if (isLoading) return <div className="container"><Loading /></div>

  return (
    <div className="admin">
      <nav className="admin__nav" aria-label="Yönetim">
        <strong style={{ padding: 'var(--space-2) var(--space-3)' }}>Otel Yönetimi</strong>
        {nav.filter((n) => !n.role || hasRole(n.role)).map((n) => (
          <NavLink key={n.to} to={n.to} end={n.end}>{n.label}</NavLink>
        ))}
        <div style={{ marginTop: 'auto' }} className="stack small">
          <span className="muted">{user?.email}</span>
          <Button size="sm" onClick={logout}>Çıkış</Button>
        </div>
      </nav>
      <main className="admin__main"><Outlet /></main>
    </div>
  )
}
