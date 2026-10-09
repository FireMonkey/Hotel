import { useQuery } from '@tanstack/react-query'
import { Link } from 'react-router'
import { get } from '@/api/client'
import type { PublicRoomType } from '@/api/types'
import { RoomCard } from '@/components/RoomCard'
import { ErrorMessage, Loading } from '@/components/ui'

export default function RoomsPage() {
  const rooms = useQuery({ queryKey: ['public', 'room-types'], queryFn: () => get<PublicRoomType[]>('/api/public/room-types') })
  return (
    <div className="stack">
      <h1>Odalar</h1>
      {rooms.isLoading && <Loading />}
      <ErrorMessage error={rooms.error} />
      <div className="grid">
        {rooms.data?.map((r) => (
          <RoomCard key={r.id} room={r} footer={<Link className="btn btn--primary" to="/search">Müsaitlik ara</Link>} />
        ))}
      </div>
    </div>
  )
}
