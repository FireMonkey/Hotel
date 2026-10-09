import type { PublicBooking } from '@/api/types'
import { date, money } from '@/lib/format'
import { BookingStatusBadge } from './StatusBadge'
import { Card } from './ui'

export function BookingSummary({ booking }: { booking: PublicBooking }) {
  return (
    <Card title={<span className="row">{booking.reference} <BookingStatusBadge status={booking.status} /></span>}>
      <dl className="form-grid small" style={{ margin: 0 }}>
        <div><dt className="muted">Misafir</dt><dd>{booking.guestName}</dd></div>
        <div><dt className="muted">Oda</dt><dd>{booking.roomTypeName}{booking.ratePlanName && ` · ${booking.ratePlanName}`}</dd></div>
        <div><dt className="muted">Tarihler</dt><dd>{date(booking.checkIn)} → {date(booking.checkOut)} ({booking.nights} gece)</dd></div>
        <div><dt className="muted">Kişi</dt><dd>{booking.adults} yetişkin{booking.children > 0 && `, ${booking.children} çocuk`}</dd></div>
        <div><dt className="muted">Toplam</dt><dd><strong>{money(booking.totalAmount, booking.currency)}</strong></dd></div>
      </dl>
    </Card>
  )
}
