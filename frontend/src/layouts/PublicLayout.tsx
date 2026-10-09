import { Link, NavLink, Outlet } from 'react-router'
import { useHotel } from '@/api/hooks'

export default function PublicLayout() {
  const { data: hotel } = useHotel()
  return (
    <>
      <header className="site-header">
        <div className="container site-header__inner">
          <Link to="/" style={{ fontWeight: 700, textDecoration: 'none' }}>{hotel?.name ?? 'Hotel'}</Link>
          <nav>
            <NavLink to="/rooms">Odalar</NavLink>
            <NavLink to="/my-booking">Rezervasyonum</NavLink>
            <NavLink to="/search" className="btn btn--primary btn--sm">Rezervasyon yap</NavLink>
          </nav>
        </div>
      </header>
      <main className="site-main">
        <div className="container"><Outlet /></div>
      </main>
      <footer className="site-footer">
        <div className="container row row--between small">
          <span>{hotel?.name} · {hotel?.address} {hotel?.city}</span>
          <span>{hotel?.phone} · {hotel?.email}</span>
        </div>
      </footer>
    </>
  )
}
