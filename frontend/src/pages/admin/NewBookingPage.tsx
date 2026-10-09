import { useState } from 'react'
import { useMutation, useQuery } from '@tanstack/react-query'
import { useNavigate } from 'react-router'
import { get, post, qs } from '@/api/client'
import type { Booking, Guest, RoomTypeAvailability } from '@/api/types'
import { emptyGuest, GuestFields } from '@/components/GuestFields'
import { defaultSearch } from '@/components/SearchForm'
import { Alert, Button, Card, Checkbox, ErrorMessage, Field, Input, Loading, PageHeader, Select, Textarea } from '@/components/ui'
import { addDays, money } from '@/lib/format'
import { mealPlanLabel } from '@/lib/labels'

export default function NewBookingPage() {
  const navigate = useNavigate()
  const [search, setSearch] = useState(defaultSearch)
  const [selected, setSelected] = useState<{ roomTypeId: string; ratePlanId: string } | null>(null)
  const [guest, setGuest] = useState<Guest>(emptyGuest)
  const [override, setOverride] = useState('')
  const [notes, setNotes] = useState('')
  const [requests, setRequests] = useState('')
  const [confirmNow, setConfirmNow] = useState(true)

  const avail = useQuery({
    queryKey: ['availability', search],
    queryFn: () => get<RoomTypeAvailability[]>(`/api/public/availability${qs(search)}`),
    enabled: search.checkOut > search.checkIn,
  })
  const rate = avail.data?.find((r) => r.roomTypeId === selected?.roomTypeId)?.rates.find((r) => r.ratePlanId === selected?.ratePlanId)

  const create = useMutation({
    mutationFn: () => post<Booking>('/api/admin/bookings', {
      confirm: confirmNow,
      booking: {
        ratePlanId: selected!.ratePlanId, ...search, guest,
        specialRequests: requests || null, internalNotes: notes || null,
        totalAmountOverride: override ? Number(override) : null,
      },
    }),
    onSuccess: (b) => navigate(`/admin/bookings/${b.id}`),
  })

  return (
    <div className="stack">
      <PageHeader title="Yeni rezervasyon" />
      <Card title="1. Tarih ve kişi">
        <div className="form-grid">
          <Field label="Giriş"><Input type="date" value={search.checkIn} onChange={(e) => {
            const checkIn = e.target.value
            setSearch((s) => ({ ...s, checkIn, checkOut: s.checkOut <= checkIn ? addDays(checkIn, 1) : s.checkOut }))
          }} /></Field>
          <Field label="Çıkış"><Input type="date" min={addDays(search.checkIn, 1)} value={search.checkOut} onChange={(e) => setSearch((s) => ({ ...s, checkOut: e.target.value }))} /></Field>
          <Field label="Yetişkin"><Select value={search.adults} onChange={(e) => setSearch((s) => ({ ...s, adults: Number(e.target.value) }))}>
            {[1, 2, 3, 4, 5, 6].map((n) => <option key={n}>{n}</option>)}</Select></Field>
          <Field label="Çocuk"><Select value={search.children} onChange={(e) => setSearch((s) => ({ ...s, children: Number(e.target.value) }))}>
            {[0, 1, 2, 3, 4].map((n) => <option key={n}>{n}</option>)}</Select></Field>
        </div>
      </Card>

      <Card title="2. Oda ve fiyat">
        {avail.isLoading && <Loading />}
        <ErrorMessage error={avail.error} />
        {avail.data?.length === 0 && <Alert tone="warning">Uygun oda yok.</Alert>}
        <div className="stack" style={{ gap: 'var(--space-2)' }}>
          {avail.data?.flatMap((room) => room.rates.map((r) => (
            <label key={r.ratePlanId} className="row row--between card" style={{ boxShadow: 'none', cursor: 'pointer' }}>
              <span className="row">
                <input type="radio" name="rate" checked={selected?.ratePlanId === r.ratePlanId}
                  onChange={() => setSelected({ roomTypeId: room.roomTypeId, ratePlanId: r.ratePlanId })} />
                <span><strong>{room.name}</strong> · {r.name} <span className="small muted">({mealPlanLabel[r.mealPlan]}, {room.availableRooms} müsait)</span></span>
              </span>
              <strong>{money(r.totalPrice, r.currency)}</strong>
            </label>
          )))}
        </div>
      </Card>

      {selected && rate && (
        <Card title="3. Misafir">
          <div className="stack">
            <GuestFields value={guest} onChange={setGuest} />
            <div className="form-grid">
              <Field label="Fiyatı değiştir" hint={`Hesaplanan: ${money(rate.totalPrice, rate.currency)}`}>
                <Input type="number" min={0} step="0.01" value={override} onChange={(e) => setOverride(e.target.value)} placeholder="Boş = hesaplanan" />
              </Field>
            </div>
            <Field label="Özel istekler"><Textarea value={requests} onChange={(e) => setRequests(e.target.value)} /></Field>
            <Field label="İç notlar"><Textarea value={notes} onChange={(e) => setNotes(e.target.value)} /></Field>
            <Checkbox label="Hemen onayla" checked={confirmNow} onChange={(e) => setConfirmNow(e.target.checked)} />
            <ErrorMessage error={create.error} />
            <Button variant="primary" disabled={create.isPending || !guest.firstName || !guest.lastName || !guest.email} onClick={() => create.mutate()}>
              Rezervasyonu oluştur
            </Button>
          </div>
        </Card>
      )}
    </div>
  )
}
