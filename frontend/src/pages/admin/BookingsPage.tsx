import { useQuery } from '@tanstack/react-query'
import { Link, useNavigate, useSearchParams } from 'react-router'
import { get, qs } from '@/api/client'
import type { Booking, BookingSource, BookingStatus, Paged } from '@/api/types'
import { BookingStatusBadge, PaymentBadge } from '@/components/StatusBadge'
import { Badge, ErrorMessage, Field, Input, Loading, PageHeader, Pagination, Select } from '@/components/ui'
import { date, money } from '@/lib/format'
import { bookingStatusLabel, sourceLabel } from '@/lib/labels'

export default function BookingsPage() {
  const [params, setParams] = useSearchParams()
  const navigate = useNavigate()
  const filters = {
    search: params.get('search') ?? '',
    status: params.get('status') ?? '',
    source: params.get('source') ?? '',
    arrivalFrom: params.get('arrival') ?? params.get('arrivalFrom') ?? '',
    arrivalTo: params.get('arrival') ?? params.get('arrivalTo') ?? '',
    overbooked: params.get('overbooked') ? true : undefined,
    page: Number(params.get('page') ?? 1),
    pageSize: 25,
  }
  const set = (key: string, value: string) => {
    const next = new URLSearchParams(params)
    if (value) next.set(key, value); else next.delete(key)
    if (key !== 'page') next.delete('page')
    if (key.startsWith('arrival')) next.delete('arrival')
    setParams(next, { replace: true })
  }

  const q = useQuery({
    queryKey: ['admin', 'bookings', filters],
    queryFn: () => get<Paged<Booking>>(`/api/admin/bookings${qs(filters)}`),
    placeholderData: (prev) => prev,
  })

  return (
    <div className="stack">
      <PageHeader title="Rezervasyonlar" actions={<Link className="btn btn--primary" to="/admin/bookings/new">+ Yeni rezervasyon</Link>} />

      <div className="form-grid card">
        <Field label="Ara"><Input placeholder="No, misafir, e-posta, OTA no" defaultValue={filters.search}
          onKeyDown={(e) => e.key === 'Enter' && set('search', e.currentTarget.value)} onBlur={(e) => set('search', e.target.value)} /></Field>
        <Field label="Durum">
          <Select value={filters.status} onChange={(e) => set('status', e.target.value)}>
            <option value="">Tümü</option>
            {(Object.keys(bookingStatusLabel) as BookingStatus[]).map((s) => <option key={s} value={s}>{bookingStatusLabel[s]}</option>)}
          </Select>
        </Field>
        <Field label="Kaynak">
          <Select value={filters.source} onChange={(e) => set('source', e.target.value)}>
            <option value="">Tümü</option>
            {(Object.keys(sourceLabel) as BookingSource[]).map((s) => <option key={s} value={s}>{sourceLabel[s]}</option>)}
          </Select>
        </Field>
        <Field label="Giriş (başlangıç)"><Input type="date" value={filters.arrivalFrom} onChange={(e) => set('arrivalFrom', e.target.value)} /></Field>
        <Field label="Giriş (bitiş)"><Input type="date" value={filters.arrivalTo} onChange={(e) => set('arrivalTo', e.target.value)} /></Field>
      </div>

      {q.isLoading && <Loading />}
      <ErrorMessage error={q.error} />
      {q.data && (
        <>
          <div className="table-wrap">
            <table className="table">
              <thead>
                <tr><th>No</th><th>Misafir</th><th>Oda</th><th>Giriş</th><th>Çıkış</th><th>Kaynak</th><th className="right">Tutar</th><th>Durum</th><th>Ödeme</th></tr>
              </thead>
              <tbody>
                {q.data.items.map((b) => (
                  <tr key={b.id} className="is-clickable" onClick={() => navigate(`/admin/bookings/${b.id}`)}>
                    <td><Link to={`/admin/bookings/${b.id}`} onClick={(e) => e.stopPropagation()}>{b.reference}</Link>
                      {b.isOverbooked && <> <Badge tone="danger">Overbooking</Badge></>}</td>
                    <td>{b.guest.firstName} {b.guest.lastName}</td>
                    <td>{b.roomTypeName}{b.roomNumber && ` · ${b.roomNumber}`}</td>
                    <td>{date(b.checkIn)}</td>
                    <td>{date(b.checkOut)}</td>
                    <td>{sourceLabel[b.source]}{b.externalReference && <div className="small muted">{b.externalReference}</div>}</td>
                    <td className="right">{money(b.totalAmount, b.currency)}</td>
                    <td><BookingStatusBadge status={b.status} /></td>
                    <td><PaymentBadge status={b.paymentStatus} /></td>
                  </tr>
                ))}
                {q.data.items.length === 0 && <tr><td colSpan={9} className="muted">Kayıt bulunamadı</td></tr>}
              </tbody>
            </table>
          </div>
          <Pagination page={q.data.page} pageSize={q.data.pageSize} total={q.data.totalCount} onPage={(p) => set('page', String(p))} />
        </>
      )}
    </div>
  )
}
