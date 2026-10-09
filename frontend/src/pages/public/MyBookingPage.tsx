import { useState, type FormEvent } from 'react'
import { useMutation } from '@tanstack/react-query'
import { ApiError, post } from '@/api/client'
import type { PublicBooking } from '@/api/types'
import { BookingSummary } from '@/components/BookingSummary'
import { Alert, Button, Card, ErrorMessage, Field, Input } from '@/components/ui'

export default function MyBookingPage() {
  const [form, setForm] = useState({ reference: '', email: '' })
  const [booking, setBooking] = useState<PublicBooking | null>(null)

  const lookup = useMutation({ mutationFn: () => post<PublicBooking>('/api/public/bookings/lookup', form), onSuccess: setBooking })
  const cancel = useMutation({ mutationFn: () => post<PublicBooking>('/api/public/bookings/cancel', form), onSuccess: setBooking })

  const submit = (e: FormEvent) => { e.preventDefault(); setBooking(null); lookup.mutate() }
  const notFound = lookup.error instanceof ApiError && lookup.error.status === 404

  return (
    <div className="stack" style={{ maxWidth: 640 }}>
      <h1>Rezervasyonum</h1>
      <Card>
        <form className="form-grid" onSubmit={submit} style={{ alignItems: 'end' }}>
          <Field label="Rezervasyon no"><Input required value={form.reference} onChange={(e) => setForm({ ...form, reference: e.target.value })} /></Field>
          <Field label="E-posta"><Input required type="email" value={form.email} onChange={(e) => setForm({ ...form, email: e.target.value })} /></Field>
          <Button type="submit" variant="primary" disabled={lookup.isPending}>Görüntüle</Button>
        </form>
      </Card>
      {notFound ? <Alert>Rezervasyon bulunamadı. Bilgileri kontrol edin.</Alert> : <ErrorMessage error={lookup.error} />}
      {booking && (
        <>
          <BookingSummary booking={booking} />
          {booking.canCancel && (
            <Button variant="danger" disabled={cancel.isPending}
              onClick={() => { if (confirm('Rezervasyonu iptal etmek istediğinize emin misiniz?')) cancel.mutate() }}>
              Rezervasyonu iptal et
            </Button>
          )}
          {!booking.canCancel && booking.status !== 'Cancelled' && (
            <p className="small muted">Bu rezervasyon online iptal edilemez. Lütfen otelle iletişime geçin.</p>
          )}
          <ErrorMessage error={cancel.error} />
        </>
      )}
    </div>
  )
}
