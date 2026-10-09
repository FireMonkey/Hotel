import { useQuery } from '@tanstack/react-query'
import { Link } from 'react-router'
import { get } from '@/api/client'
import type { Dashboard } from '@/api/types'
import { Alert, Badge, Card, ErrorMessage, Loading, PageHeader } from '@/components/ui'
import { date, dateTime, money, weekday } from '@/lib/format'
import { sourceLabel } from '@/lib/labels'

export default function DashboardPage() {
  const q = useQuery({ queryKey: ['admin', 'dashboard'], queryFn: () => get<Dashboard>('/api/admin/dashboard?days=30'), refetchInterval: 60_000 })
  if (q.isLoading) return <Loading />
  if (!q.data) return <ErrorMessage error={q.error} />
  const d = q.data
  const todayOcc = d.occupancy[0]

  return (
    <div className="stack">
      <PageHeader title="Gösterge paneli" actions={<Link className="btn btn--primary" to="/admin/bookings/new">+ Yeni rezervasyon</Link>} />

      {d.overbookedBookings > 0 && (
        <Alert>⚠ {d.overbookedBookings} rezervasyon overbooking durumunda. <Link to="/admin/bookings?overbooked=1">İncele</Link></Alert>
      )}

      <div className="grid" style={{ gridTemplateColumns: 'repeat(auto-fill, minmax(160px, 1fr))' }}>
        <Stat label="Bugün doluluk" value={`%${todayOcc?.percent ?? 0}`} sub={`${todayOcc?.booked ?? 0}/${d.totalRooms} oda`} />
        <Stat label="Bugün giriş" value={d.arrivals} link={`/admin/bookings?arrival=${d.date}`} />
        <Stat label="Bugün çıkış" value={d.departures} />
        <Stat label="Konaklayan" value={d.inHouse} link="/admin/bookings?status=CheckedIn" />
        <Stat label="Onay bekleyen" value={d.pendingBookings} link="/admin/bookings?status=Pending" />
      </div>

      <Card title="30 günlük doluluk">
        <div className="stack" style={{ gap: 'var(--space-1)' }}>
          {d.occupancy.map((o) => (
            <div key={o.date} className="row small" style={{ flexWrap: 'nowrap' }}>
              <span style={{ width: 110 }} className="muted">{weekday(o.date)} {date(o.date, { day: '2-digit', month: 'short' })}</span>
              <div className="bar" style={{ flex: 1 }}><div className="bar__fill" style={{ width: `${o.percent}%` }} /></div>
              <span style={{ width: 80 }} className="right">%{o.percent} ({o.booked})</span>
            </div>
          ))}
        </div>
      </Card>

      <div className="grid" style={{ gridTemplateColumns: 'repeat(auto-fit, minmax(320px, 1fr))' }}>
        <Card title="Kaynağa göre gelir (son 30 gün girişli)">
          <table className="table">
            <thead><tr><th>Kaynak</th><th className="right">Rez.</th><th className="right">Ciro</th><th className="right">Komisyon</th></tr></thead>
            <tbody>
              {d.revenueBySource.map((r) => (
                <tr key={r.source}>
                  <td>{sourceLabel[r.source]}</td>
                  <td className="right">{r.bookings}</td>
                  <td className="right">{money(r.revenue)}</td>
                  <td className="right">{money(r.commission)}</td>
                </tr>
              ))}
              {d.revenueBySource.length === 0 && <tr><td colSpan={4} className="muted">Veri yok</td></tr>}
            </tbody>
          </table>
        </Card>

        <Card title="Kanal durumu" actions={<Link to="/admin/channels" className="small">Yönet</Link>}>
          <table className="table">
            <thead><tr><th>Kanal</th><th>Son çekme</th><th>Son gönderim</th><th /></tr></thead>
            <tbody>
              {d.channels.map((c) => (
                <tr key={c.id}>
                  <td><Link to={`/admin/channels/${c.id}`}>{c.name}</Link></td>
                  <td className="small">{dateTime(c.lastPull)}</td>
                  <td className="small">{dateTime(c.lastPush)}</td>
                  <td>{!c.isEnabled ? <Badge>Kapalı</Badge> : c.lastError ? <Badge tone="danger">Hata</Badge> : <Badge tone="success">OK</Badge>}</td>
                </tr>
              ))}
              {d.channels.length === 0 && <tr><td colSpan={4} className="muted">Henüz kanal bağlanmadı</td></tr>}
            </tbody>
          </table>
        </Card>
      </div>
    </div>
  )
}

function Stat({ label, value, sub, link }: { label: string; value: string | number; sub?: string; link?: string }) {
  const body = (
    <div className="card stat">
      <span className="stat__label">{label}</span>
      <span className="stat__value">{value}</span>
      {sub && <span className="small muted">{sub}</span>}
    </div>
  )
  return link ? <Link to={link} style={{ textDecoration: 'none', color: 'inherit' }}>{body}</Link> : body
}
