import { Fragment, useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { get, put, qs } from '@/api/client'
import type { AriCalendar } from '@/api/types'
import { useAdminRoomTypes } from '@/api/hooks'
import { Alert, Button, Checkbox, ErrorMessage, Field, Input, Loading, Modal, PageHeader, Select } from '@/components/ui'
import { addDays, date, parseDate, today, weekday } from '@/lib/format'

const DAYS = 21
const weekdays = [
  { v: 1, l: 'Pzt' }, { v: 2, l: 'Sal' }, { v: 3, l: 'Çar' }, { v: 4, l: 'Per' }, { v: 5, l: 'Cum' }, { v: 6, l: 'Cmt' }, { v: 0, l: 'Paz' },
]
const DayNames = ['Sunday', 'Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday']

export default function CalendarPage() {
  const [from, setFrom] = useState(today())
  const to = addDays(from, DAYS)
  const [bulkOpen, setBulkOpen] = useState(false)
  const [invOpen, setInvOpen] = useState(false)
  const q = useQuery({ queryKey: ['admin', 'ari', from], queryFn: () => get<AriCalendar>(`/api/admin/ari/calendar${qs({ from, to })}`) })
  const dates = Array.from({ length: DAYS }, (_, i) => addDays(from, i))
  const isWeekend = (d: string) => [0, 6].includes(parseDate(d).getDay())

  return (
    <div className="stack">
      <PageHeader title="Fiyat & müsaitlik" actions={<>
        <Button onClick={() => setInvOpen(true)}>Envanter kısıtla</Button>
        <Button variant="primary" onClick={() => setBulkOpen(true)}>Toplu fiyat / kısıt güncelle</Button>
      </>}>
        <p className="small muted" style={{ margin: 0 }}>Değişiklikler birkaç saniye içinde bağlı tüm kanallara (Booking.com, Expedia, Channex…) otomatik gönderilir. Gri fiyatlar varsayılan (taban fiyat × plan katsayısı).</p>
      </PageHeader>

      <div className="row">
        <Button size="sm" onClick={() => setFrom(addDays(from, -DAYS))}>‹ Önceki</Button>
        <Input type="date" value={from} onChange={(e) => e.target.value && setFrom(e.target.value)} style={{ width: 'auto' }} />
        <Button size="sm" onClick={() => setFrom(addDays(from, DAYS))}>Sonraki ›</Button>
        <Button size="sm" onClick={() => setFrom(today())}>Bugün</Button>
      </div>

      {q.isLoading && <Loading />}
      <ErrorMessage error={q.error} />
      {q.data && (
        <div className="table-wrap card" style={{ padding: 0 }}>
          <table className="ari">
            <thead>
              <tr>
                <th />
                {dates.map((d) => <th key={d} className={isWeekend(d) ? 'is-weekend' : ''}>{weekday(d)}<br />{date(d, { day: '2-digit', month: '2-digit' })}</th>)}
              </tr>
            </thead>
            <tbody>
              {q.data.roomTypes.map((rt) => (
                <Fragment key={rt.roomTypeId}>
                  <tr>
                    <td><strong>{rt.name}</strong><div className="small muted">müsait / satılabilir</div></td>
                    {rt.inventory.map((c) => (
                      <td key={c.date} className={[c.available === 0 ? 'is-zero' : c.available <= 2 ? 'is-low' : '', isWeekend(c.date) ? 'is-weekend' : ''].join(' ')}
                        title={`Fiziksel ${c.physicalRooms}, satılabilir ${c.sellable}, dolu ${c.booked}`}>
                        {c.available}<span className="muted">/{c.sellable}{c.hasOverride && '*'}</span>
                      </td>
                    ))}
                  </tr>
                  {rt.ratePlans.map((rp) => (
                    <tr key={rp.ratePlanId}>
                      <td className="small" style={{ paddingLeft: 16 }}>{rp.name}</td>
                      {rp.days.map((d) => (
                        <td key={d.date}
                          className={[d.stopSell ? 'is-closed' : '', d.isDefault ? 'is-default' : '', isWeekend(d.date) ? 'is-weekend' : ''].join(' ')}
                          title={[d.stopSell && 'Satış kapalı', d.closedToArrival && 'Girişe kapalı', d.closedToDeparture && 'Çıkışa kapalı', d.minStay > 1 && `Min ${d.minStay} gece`].filter(Boolean).join(', ')}>
                          {Math.round(d.price)}
                          {(d.minStay > 1 || d.closedToArrival || d.closedToDeparture) && (
                            <div className="small muted">{d.minStay > 1 && `m${d.minStay}`}{d.closedToArrival && ' CTA'}{d.closedToDeparture && ' CTD'}</div>
                          )}
                        </td>
                      ))}
                    </tr>
                  ))}
                </Fragment>
              ))}
            </tbody>
          </table>
        </div>
      )}

      <BulkRateModal open={bulkOpen} onClose={() => setBulkOpen(false)} defaultFrom={from} />
      <InventoryModal open={invOpen} onClose={() => setInvOpen(false)} defaultFrom={from} />
    </div>
  )
}

function BulkRateModal({ open, onClose, defaultFrom }: { open: boolean; onClose: () => void; defaultFrom: string }) {
  const qc = useQueryClient()
  const types = useAdminRoomTypes()
  const [plans, setPlans] = useState<string[]>([])
  const [range, setRange] = useState({ from: defaultFrom, to: addDays(defaultFrom, 7) })
  const [days, setDays] = useState<number[]>([])
  const [price, setPrice] = useState('')
  const [minStay, setMinStay] = useState('')
  const [stopSell, setStopSell] = useState<'' | 'true' | 'false'>('')
  const [cta, setCta] = useState<'' | 'true' | 'false'>('')
  const [ctd, setCtd] = useState<'' | 'true' | 'false'>('')
  const tri = (v: string) => (v === '' ? null : v === 'true')

  const save = useMutation({
    mutationFn: () => put<{ updated: number }>('/api/admin/ari/rates', {
      ratePlanIds: plans,
      from: range.from,
      to: addDays(range.to, 1), // UI end date is inclusive
      weekdays: days.length ? days.map((d) => DayNames[d]) : null,
      price: price ? Number(price) : null,
      minStay: minStay ? Number(minStay) : null,
      stopSell: tri(stopSell), closedToArrival: tri(cta), closedToDeparture: tri(ctd),
    }),
    onSuccess: () => { qc.invalidateQueries({ queryKey: ['admin', 'ari'] }); onClose() },
  })

  const togglePlan = (id: string) => setPlans((p) => (p.includes(id) ? p.filter((x) => x !== id) : [...p, id]))

  return (
    <Modal open={open} onClose={onClose} title="Toplu güncelleme">
      <div className="stack">
        <Field label="Fiyat planları">
          <div className="stack" style={{ gap: 'var(--space-1)' }}>
            {types.data?.map((t) => t.ratePlans.filter((p) => p.isActive).map((p) => (
              <Checkbox key={p.id} label={`${t.name} · ${p.name}`} checked={plans.includes(p.id)} onChange={() => togglePlan(p.id)} />
            )))}
          </div>
        </Field>
        <div className="form-grid">
          <Field label="Başlangıç"><Input type="date" value={range.from} onChange={(e) => setRange({ ...range, from: e.target.value })} /></Field>
          <Field label="Bitiş (dahil)"><Input type="date" value={range.to} min={range.from} onChange={(e) => setRange({ ...range, to: e.target.value })} /></Field>
        </div>
        <Field label="Günler" hint="Hiçbiri seçili değilse tüm günler">
          <div className="row">{weekdays.map((w) => (
            <Checkbox key={w.v} label={w.l} checked={days.includes(w.v)} onChange={() => setDays((d) => (d.includes(w.v) ? d.filter((x) => x !== w.v) : [...d, w.v]))} />
          ))}</div>
        </Field>
        <div className="form-grid">
          <Field label="Gecelik fiyat"><Input type="number" min={0} step="0.01" value={price} onChange={(e) => setPrice(e.target.value)} placeholder="Değiştirme" /></Field>
          <Field label="Min. konaklama"><Input type="number" min={1} value={minStay} onChange={(e) => setMinStay(e.target.value)} placeholder="Değiştirme" /></Field>
          <TriSelect label="Satış" value={stopSell} onChange={setStopSell} on="Kapat" off="Aç" />
          <TriSelect label="Girişe kapalı (CTA)" value={cta} onChange={setCta} on="Evet" off="Hayır" />
          <TriSelect label="Çıkışa kapalı (CTD)" value={ctd} onChange={setCtd} on="Evet" off="Hayır" />
        </div>
        <ErrorMessage error={save.error} />
        <Button variant="primary" disabled={!plans.length || save.isPending} onClick={() => save.mutate()}>Uygula</Button>
      </div>
    </Modal>
  )
}

function TriSelect({ label, value, onChange, on, off }: { label: string; value: '' | 'true' | 'false'; onChange: (v: '' | 'true' | 'false') => void; on: string; off: string }) {
  return (
    <Field label={label}>
      <Select value={value} onChange={(e) => onChange(e.target.value as '' | 'true' | 'false')}>
        <option value="">Değiştirme</option>
        <option value="true">{on}</option>
        <option value="false">{off}</option>
      </Select>
    </Field>
  )
}

function InventoryModal({ open, onClose, defaultFrom }: { open: boolean; onClose: () => void; defaultFrom: string }) {
  const qc = useQueryClient()
  const types = useAdminRoomTypes()
  const [roomTypeId, setRoomTypeId] = useState('')
  const [range, setRange] = useState({ from: defaultFrom, to: addDays(defaultFrom, 7) })
  const [max, setMax] = useState('')
  const save = useMutation({
    mutationFn: () => put('/api/admin/ari/inventory', { roomTypeId, from: range.from, to: addDays(range.to, 1), maxSellable: max === '' ? null : Number(max) }),
    onSuccess: () => { qc.invalidateQueries({ queryKey: ['admin', 'ari'] }); onClose() },
  })
  return (
    <Modal open={open} onClose={onClose} title="Satılabilir oda sayısını kısıtla">
      <div className="stack">
        <Alert tone="warning">Örn. 8 standart odanın sadece 5'ini satışa açmak için. Boş bırakırsanız kısıt kaldırılır.</Alert>
        <Field label="Oda tipi">
          <Select value={roomTypeId} onChange={(e) => setRoomTypeId(e.target.value)}>
            <option value="">Seçin</option>
            {types.data?.map((t) => <option key={t.id} value={t.id}>{t.name} ({t.roomCount} oda)</option>)}
          </Select>
        </Field>
        <div className="form-grid">
          <Field label="Başlangıç"><Input type="date" value={range.from} onChange={(e) => setRange({ ...range, from: e.target.value })} /></Field>
          <Field label="Bitiş (dahil)"><Input type="date" value={range.to} min={range.from} onChange={(e) => setRange({ ...range, to: e.target.value })} /></Field>
          <Field label="En fazla satılacak"><Input type="number" min={0} value={max} onChange={(e) => setMax(e.target.value)} /></Field>
        </div>
        <ErrorMessage error={save.error} />
        <Button variant="primary" disabled={!roomTypeId || save.isPending} onClick={() => save.mutate()}>Kaydet</Button>
      </div>
    </Modal>
  )
}
