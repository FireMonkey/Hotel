import { useState, type FormEvent } from 'react'
import { useMutation, useQuery } from '@tanstack/react-query'
import { Link, useNavigate, useSearchParams } from 'react-router'
import { get, post, qs } from '@/api/client'
import type { Guest, PublicBooking, RoomTypeAvailability } from '@/api/types'
import { emptyGuest, GuestFields } from '@/components/GuestFields'
import { Alert, Button, Card, ErrorMessage, Field, Loading, Textarea } from '@/components/ui'
import { date, money } from '@/lib/format'
import { mealPlanLabel } from '@/lib/labels'

export default function BookPage() {
  const [params] = useSearchParams()
  const navigate = useNavigate()
  const search = {
    checkIn: params.get('checkIn') ?? '',
    checkOut: params.get('checkOut') ?? '',
    adults: Number(params.get('adults') ?? 2),
    children: Number(params.get('children') ?? 0),
    roomTypeId: params.get('roomTypeId') ?? '',
  }
  const ratePlanId = params.get('ratePlanId') ?? ''
  const [guest, setGuest] = useState<Guest>(emptyGuest)
  const [requests, setRequests] = useState('')
  const [accepted, setAccepted] = useState(false)

  const quote = useQuery({
    queryKey: ['availability', search],
    queryFn: () => get<RoomTypeAvailability[]>(`/api/public/availability${qs(search)}`),
    enabled: !!search.roomTypeId,
  })
  const room = quote.data?.[0]
  const rate = room?.rates.find((r) => r.ratePlanId === ratePlanId)

  const book = useMutation({
    mutationFn: () => post<PublicBooking>('/api/public/bookings', {
      ratePlanId, checkIn: search.checkIn, checkOut: search.checkOut, adults: search.adults, children: search.children,
      guest, specialRequests: requests || null,
    }),
    onSuccess: (b) => navigate(`/booking/${b.reference}`, { state: b }),
  })

  const submit = (e: FormEvent) => { e.preventDefault(); book.mutate() }

  if (quote.isLoading) return <Loading />
  if (!room || !rate) {
    return <Alert tone="warning">Bu oda seçilen tarihlerde artık müsait değil. <Link to={`/search${qs(search)}`}>Aramaya dön</Link></Alert>
  }

  return (
    <div className="grid" style={{ gridTemplateColumns: 'minmax(0, 2fr) minmax(260px, 1fr)', alignItems: 'start' }}>
      <form className="stack" onSubmit={submit}>
        <h1>Misafir bilgileri</h1>
        <Card><GuestFields value={guest} onChange={setGuest} /></Card>
        <Card>
          <Field label="Özel istekler" hint="Garanti edilmez, otel imkanlar dahilinde karşılanır.">
            <Textarea value={requests} maxLength={1000} onChange={(e) => setRequests(e.target.value)} />
          </Field>
        </Card>
        <label className="checkbox">
          <input type="checkbox" required checked={accepted} onChange={(e) => setAccepted(e.target.checked)} />
          <span>Rezervasyon ve iptal koşullarını, KVKK aydınlatma metnini okudum ve kabul ediyorum.</span>
        </label>
        <ErrorMessage error={book.error} />
        <Button type="submit" variant="primary" disabled={book.isPending}>
          {book.isPending ? 'Gönderiliyor…' : `Rezervasyonu tamamla · ${money(rate.totalPrice, rate.currency)}`}
        </Button>
        <p className="small muted">Ödeme otelde yapılır.</p>
      </form>

      <Card title="Rezervasyon özeti">
        <div className="stack small" style={{ gap: 'var(--space-2)' }}>
          <strong>{room.name}</strong>
          <span>{rate.name} · {mealPlanLabel[rate.mealPlan]}</span>
          <span>{date(search.checkIn)} → {date(search.checkOut)}</span>
          <span>{search.adults} yetişkin{search.children > 0 && `, ${search.children} çocuk`}</span>
          <hr style={{ width: '100%', border: 0, borderTop: '1px solid var(--color-border)' }} />
          {rate.nights.map((n) => (
            <div key={n.date} className="row row--between"><span>{date(n.date)}</span><span>{money(n.price, rate.currency)}</span></div>
          ))}
          <div className="row row--between"><strong>Toplam</strong><strong>{money(rate.totalPrice, rate.currency)}</strong></div>
          <span className="muted">
            {rate.isRefundable && rate.freeCancellationDays != null
              ? `Girişten ${rate.freeCancellationDays} gün öncesine kadar ücretsiz iptal.`
              : 'Bu fiyat iade edilemez.'}
          </span>
        </div>
      </Card>
    </div>
  )
}
