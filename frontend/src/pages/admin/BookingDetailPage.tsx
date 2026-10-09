import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Link, useParams } from 'react-router'
import { get, patch, post } from '@/api/client'
import type { AdminRoom, Booking, PaymentStatus } from '@/api/types'
import { BookingStatusBadge, PaymentBadge } from '@/components/StatusBadge'
import { Alert, Button, Card, ErrorMessage, Field, Input, Loading, Modal, PageHeader, Select, Textarea } from '@/components/ui'
import { date, dateTime, money } from '@/lib/format'
import { paymentStatusLabel, sourceLabel } from '@/lib/labels'

export default function BookingDetailPage() {
  const { id } = useParams()
  const qc = useQueryClient()
  const q = useQuery({ queryKey: ['admin', 'booking', id], queryFn: () => get<Booking>(`/api/admin/bookings/${id}`) })
  const [checkInOpen, setCheckInOpen] = useState(false)

  const onDone = (b: Booking) => {
    qc.setQueryData(['admin', 'booking', id], b)
    qc.invalidateQueries({ queryKey: ['admin', 'bookings'] })
    qc.invalidateQueries({ queryKey: ['admin', 'dashboard'] })
  }
  const action = useMutation({ mutationFn: (a: { path: string; body?: unknown }) => post<Booking>(`/api/admin/bookings/${id}/${a.path}`, a.body), onSuccess: onDone })

  if (q.isLoading) return <Loading />
  if (!q.data) return <ErrorMessage error={q.error} />
  const b = q.data
  const fromChannel = !['Direct', 'Admin'].includes(b.source)

  return (
    <div className="stack">
      <PageHeader title={<span className="row">{b.reference} <BookingStatusBadge status={b.status} /></span>}
        actions={<>
          {b.status === 'Pending' && <Button variant="primary" onClick={() => action.mutate({ path: 'confirm' })}>Onayla</Button>}
          {(b.status === 'Pending' || b.status === 'Confirmed') && <Button variant="primary" onClick={() => setCheckInOpen(true)}>Check-in</Button>}
          {b.status === 'CheckedIn' && <Button variant="primary" onClick={() => action.mutate({ path: 'check-out' })}>Check-out</Button>}
          {(b.status === 'Pending' || b.status === 'Confirmed') && (
            <>
              <Button onClick={() => confirm('Gelmedi (no-show) olarak işaretlensin mi?') && action.mutate({ path: 'no-show' })}>No-show</Button>
              <Button variant="danger" onClick={() => {
                const reason = prompt(fromChannel ? 'OTA rezervasyonları normalde kanal üzerinden iptal edilir. Yine de iptal nedeni:' : 'İptal nedeni:')
                if (reason !== null) action.mutate({ path: 'cancel', body: { reason } })
              }}>İptal et</Button>
            </>
          )}
        </>}>
        <Link to="/admin/bookings" className="small">← Rezervasyonlar</Link>
      </PageHeader>

      <ErrorMessage error={action.error} />
      {b.isOverbooked && b.status !== 'Cancelled' && (
        <Alert>Bu rezervasyon kanaldan müsaitlik yokken geldi (overbooking). Oda ataması / misafir ile iletişim gerekli.</Alert>
      )}

      <div className="grid" style={{ gridTemplateColumns: 'repeat(auto-fit, minmax(300px, 1fr))', alignItems: 'start' }}>
        <Card title="Konaklama">
          <dl className="stack small" style={{ margin: 0, gap: 'var(--space-2)' }}>
            <Row k="Oda tipi" v={`${b.roomTypeName}${b.ratePlanName ? ` · ${b.ratePlanName}` : ''}`} />
            <Row k="Oda" v={b.roomNumber ?? 'Atanmadı'} />
            <Row k="Tarihler" v={`${date(b.checkIn)} → ${date(b.checkOut)} (${b.nights} gece)`} />
            <Row k="Kişi" v={`${b.adults} yetişkin, ${b.children} çocuk`} />
            <Row k="Kaynak" v={`${sourceLabel[b.source]}${b.channelName ? ` (${b.channelName})` : ''}`} />
            {b.externalReference && <Row k="OTA no" v={b.externalReference} />}
            <Row k="Oluşturma" v={dateTime(b.createdAt)} />
            {b.cancelledAt && <Row k="İptal" v={`${dateTime(b.cancelledAt)} — ${b.cancellationReason ?? ''}`} />}
            {b.specialRequests && <Row k="Özel istek" v={b.specialRequests} />}
          </dl>
        </Card>

        <Card title="Misafir">
          <dl className="stack small" style={{ margin: 0, gap: 'var(--space-2)' }}>
            <Row k="Ad soyad" v={`${b.guest.firstName} ${b.guest.lastName}`} />
            <Row k="E-posta" v={<a href={`mailto:${b.guest.email}`}>{b.guest.email}</a>} />
            <Row k="Telefon" v={b.guest.phone ? <a href={`tel:${b.guest.phone}`}>{b.guest.phone}</a> : '—'} />
            <Row k="Ülke" v={b.guest.country ?? '—'} />
          </dl>
        </Card>

        <PaymentCard booking={b} onSaved={onDone} />
      </div>

      <CheckInModal booking={b} open={checkInOpen} onClose={() => setCheckInOpen(false)}
        onSubmit={(roomId) => action.mutate({ path: 'check-in', body: { roomId } }, { onSuccess: () => setCheckInOpen(false) })} />
    </div>
  )
}

function Row({ k, v }: { k: string; v: React.ReactNode }) {
  return <div className="row row--between" style={{ flexWrap: 'nowrap', alignItems: 'start' }}><dt className="muted">{k}</dt><dd style={{ margin: 0, textAlign: 'right' }}>{v}</dd></div>
}

function PaymentCard({ booking, onSaved }: { booking: Booking; onSaved: (b: Booking) => void }) {
  const [paid, setPaid] = useState(String(booking.paidAmount))
  const [status, setStatus] = useState<PaymentStatus>(booking.paymentStatus)
  const [notes, setNotes] = useState(booking.internalNotes ?? '')
  const save = useMutation({
    mutationFn: () => patch<Booking>(`/api/admin/bookings/${booking.id}`, { paidAmount: Number(paid), paymentStatus: status, internalNotes: notes }),
    onSuccess: onSaved,
  })
  return (
    <Card title={<span className="row">Ödeme <PaymentBadge status={booking.paymentStatus} /></span>}>
      <div className="stack">
        <div className="row row--between"><span>Toplam</span><strong>{money(booking.totalAmount, booking.currency)}</strong></div>
        {booking.commissionAmount != null && <div className="row row--between small muted"><span>Komisyon</span><span>{money(booking.commissionAmount, booking.currency)}</span></div>}
        <Field label="Tahsil edilen"><Input type="number" min={0} step="0.01" value={paid} onChange={(e) => setPaid(e.target.value)} /></Field>
        <Field label="Ödeme durumu">
          <Select value={status} onChange={(e) => setStatus(e.target.value as PaymentStatus)}>
            {(Object.keys(paymentStatusLabel) as PaymentStatus[]).map((s) => <option key={s} value={s}>{paymentStatusLabel[s]}</option>)}
          </Select>
        </Field>
        <Field label="İç notlar"><Textarea value={notes} onChange={(e) => setNotes(e.target.value)} /></Field>
        <ErrorMessage error={save.error} />
        <Button onClick={() => save.mutate()} disabled={save.isPending}>Kaydet</Button>
      </div>
    </Card>
  )
}

function CheckInModal({ booking, open, onClose, onSubmit }: { booking: Booking; open: boolean; onClose: () => void; onSubmit: (roomId: string) => void }) {
  const rooms = useQuery({ queryKey: ['admin', 'rooms'], queryFn: () => get<AdminRoom[]>('/api/admin/rooms'), enabled: open })
  const candidates = rooms.data?.filter((r) => r.roomTypeId === booking.roomTypeId && r.status === 'Available' && !r.currentBooking) ?? []
  const [roomId, setRoomId] = useState('')
  return (
    <Modal open={open} onClose={onClose} title="Check-in · oda ata">
      <div className="stack">
        {rooms.isLoading && <Loading />}
        <Field label="Oda">
          <Select value={roomId} onChange={(e) => setRoomId(e.target.value)}>
            <option value="">Seçin</option>
            {candidates.map((r) => <option key={r.id} value={r.id}>{r.number} · {r.housekeeping === 'Clean' || r.housekeeping === 'Inspected' ? 'Temiz' : 'Kirli!'}</option>)}
          </Select>
        </Field>
        {rooms.data && candidates.length === 0 && <Alert tone="warning">Bu oda tipinde boş oda yok.</Alert>}
        <Button variant="primary" disabled={!roomId} onClick={() => onSubmit(roomId)}>Check-in yap</Button>
      </div>
    </Modal>
  )
}
