import type { ReactNode } from 'react'
import type { PublicRoomType } from '@/api/types'
import { money } from '@/lib/format'
import { Card } from './ui'

export function RoomCard({ room, footer }: { room: Pick<PublicRoomType, 'name' | 'description' | 'maxAdults' | 'maxChildren' | 'sizeSquareMeters' | 'amenities' | 'imageUrls'> & { fromPrice?: number; currency?: string }; footer?: ReactNode }) {
  return (
    <Card>
      {room.imageUrls[0]
        ? <img src={room.imageUrls[0]} alt={room.name} style={{ aspectRatio: '4 / 3', maxHeight: 260, width: '100%', objectFit: 'cover', borderRadius: 'var(--radius)', marginBottom: 'var(--space-3)' }} />
        : <div aria-hidden style={{ aspectRatio: '4 / 3', maxHeight: 260, background: 'var(--color-surface-muted)', borderRadius: 'var(--radius)', marginBottom: 'var(--space-3)' }} />}
      <h3>{room.name}</h3>
      {room.description && <p className="small muted">{room.description}</p>}
      <p className="small">
        {room.maxAdults} yetişkin{room.maxChildren > 0 && ` + ${room.maxChildren} çocuk`}
        {room.sizeSquareMeters > 0 && ` · ${room.sizeSquareMeters} m²`}
      </p>
      {room.amenities.length > 0 && <p className="small muted">{room.amenities.join(' · ')}</p>}
      {room.fromPrice !== undefined && room.currency && (
        <p><span className="muted small">gecelik </span><strong>{money(room.fromPrice, room.currency)}</strong><span className="muted small">'den başlayan</span></p>
      )}
      {footer}
    </Card>
  )
}
