import { Link } from 'react-router'
import { useQuery } from '@tanstack/react-query'
import { useHotel } from '@/api/hooks'
import { get } from '@/api/client'
import type { PublicRoomType } from '@/api/types'
import { SearchForm } from '@/components/SearchForm'
import { RoomCard } from '@/components/RoomCard'
import { Card } from '@/components/ui'

export default function HomePage() {
  const { data: hotel } = useHotel()
  const rooms = useQuery({ queryKey: ['public', 'room-types'], queryFn: () => get<PublicRoomType[]>('/api/public/room-types') })

  return (
    <div className="stack" style={{ gap: 'var(--space-8)' }}>
      <section className="hero stack">
        <h1>{hotel?.name}</h1>
        {hotel?.tagline && <p className="muted" style={{ fontSize: 'var(--font-size-lg)', margin: 0 }}>{hotel.tagline}</p>}
        <SearchForm />
      </section>

      {hotel?.description && <p>{hotel.description}</p>}

      <section>
        <div className="row row--between"><h2>Odalarımız</h2><Link to="/rooms">Tümü</Link></div>
        <div className="grid">
          {rooms.data?.map((r) => <RoomCard key={r.id} room={r} />)}
        </div>
      </section>

      {hotel && hotel.facilities.length > 0 && (
        <Card title="Olanaklar">
          <ul className="row" style={{ listStyle: 'none', padding: 0, margin: 0, gap: 'var(--space-6)' }}>
            {hotel.facilities.map((f) => <li key={f}>{f}</li>)}
          </ul>
        </Card>
      )}

      {hotel && (
        <Card title="Bilgi">
          <p className="small">Giriş: {hotel.checkInTime} · Çıkış: {hotel.checkOutTime}</p>
          <p className="small">{hotel.address}, {hotel.city} · {hotel.phone} · {hotel.email}</p>
        </Card>
      )}
    </div>
  )
}
