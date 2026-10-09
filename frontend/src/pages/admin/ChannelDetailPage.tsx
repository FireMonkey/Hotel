import { Fragment, useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Link, useNavigate, useParams } from 'react-router'
import { del, get, post, put } from '@/api/client'
import type { Channel, ChannelMapping, ChannelProvider, ChannelSyncLog } from '@/api/types'
import { useAdminRoomTypes } from '@/api/hooks'
import { Alert, Badge, Button, Card, Checkbox, ErrorMessage, Field, Input, Loading, PageHeader } from '@/components/ui'
import { dateTime } from '@/lib/format'

export default function ChannelDetailPage() {
  const { id } = useParams()
  const channel = useQuery({ queryKey: ['admin', 'channel', id], queryFn: () => get<Channel>(`/api/admin/channels/${id}`) })
  const providers = useQuery({ queryKey: ['admin', 'channel-providers'], queryFn: () => get<ChannelProvider[]>('/api/admin/channels/providers') })

  if (channel.isLoading || providers.isLoading) return <Loading />
  if (!channel.data) return <ErrorMessage error={channel.error} />
  const provider = providers.data?.find((p) => p.type === channel.data.channel)

  return (
    <div className="stack">
      <PageHeader title={channel.data.name}>
        <Link to="/admin/channels" className="small">← Kanallar</Link>
        {provider && <p className="small muted" style={{ margin: 0 }}>{provider.description}</p>}
      </PageHeader>
      {channel.data.lastError && <Alert>Son hata: {channel.data.lastError}</Alert>}
      <SettingsCard key={channel.data.id + (channel.dataUpdatedAt ?? '')} channel={channel.data} provider={provider} />
      <MappingsCard channel={channel.data} />
      <SyncCard channel={channel.data} provider={provider} />
    </div>
  )
}

function SettingsCard({ channel, provider }: { channel: Channel; provider?: ChannelProvider }) {
  const qc = useQueryClient()
  const navigate = useNavigate()
  const [form, setForm] = useState({
    name: channel.name,
    isEnabled: channel.isEnabled,
    externalPropertyId: channel.externalPropertyId ?? '',
    settings: { ...channel.settings },
    secret: '',
    commissionPercent: channel.commissionPercent,
    rateMarkupPercent: channel.rateMarkupPercent,
  })
  const save = useMutation({
    mutationFn: () => put(`/api/admin/channels/${channel.id}`, { channel: channel.channel, ...form, secret: form.secret || null }),
    onSuccess: () => { qc.invalidateQueries({ queryKey: ['admin'] }); setForm((f) => ({ ...f, secret: '' })) },
  })
  const remove = useMutation({
    mutationFn: () => del(`/api/admin/channels/${channel.id}`),
    onSuccess: () => { qc.invalidateQueries({ queryKey: ['admin'] }); navigate('/admin/channels') },
  })
  const needsProperty = channel.channel !== 'ICal'

  return (
    <Card title="Bağlantı ayarları">
      <form className="stack" onSubmit={(e) => { e.preventDefault(); save.mutate() }}>
        <div className="form-grid">
          <Field label="Ad"><Input required value={form.name} onChange={(e) => setForm({ ...form, name: e.target.value })} /></Field>
          {needsProperty && (
            <Field label="Otel / property ID" hint="Kanal tarafındaki otel kimliği">
              <Input value={form.externalPropertyId} onChange={(e) => setForm({ ...form, externalPropertyId: e.target.value })} />
            </Field>
          )}
          {provider?.fields.filter((f) => !f.isSecret).map((f) => (
            <Field key={f.key} label={f.label} hint={f.help}>
              <Input placeholder={f.placeholder} required={f.required} value={form.settings[f.key] ?? ''}
                onChange={(e) => setForm({ ...form, settings: { ...form.settings, [f.key]: e.target.value } })} />
            </Field>
          ))}
          {provider?.fields.filter((f) => f.isSecret).map((f) => (
            <Field key={f.key} label={f.label} hint={channel.hasSecret ? 'Kayıtlı. Değiştirmek için yeni değer girin.' : 'Şifreli saklanır.'}>
              <Input type="password" autoComplete="new-password" value={form.secret} onChange={(e) => setForm({ ...form, secret: e.target.value })} />
            </Field>
          ))}
          <Field label="Komisyon %" hint="Net gelir raporları için">
            <Input type="number" step="0.1" min={0} max={100} value={form.commissionPercent} onChange={(e) => setForm({ ...form, commissionPercent: Number(e.target.value) })} />
          </Field>
          {provider?.supportsAriPush && (
            <Field label="Fiyat farkı %" hint="Bu kanala gönderilen fiyatlara eklenir (örn. komisyonu yansıtmak için)">
              <Input type="number" step="0.1" value={form.rateMarkupPercent} onChange={(e) => setForm({ ...form, rateMarkupPercent: Number(e.target.value) })} />
            </Field>
          )}
        </div>
        <Checkbox label="Senkronizasyon aktif" checked={form.isEnabled} onChange={(e) => setForm({ ...form, isEnabled: e.target.checked })} />
        <ErrorMessage error={save.error ?? remove.error} />
        {save.isSuccess && <Alert tone="success">Kaydedildi.</Alert>}
        <div className="row">
          <Button type="submit" variant="primary" disabled={save.isPending}>Kaydet</Button>
          <Button variant="danger" onClick={() => confirm('Kanal bağlantısı silinsin mi?') && remove.mutate()}>Sil</Button>
        </div>
      </form>
    </Card>
  )
}

interface RoomRow { externalRoomId: string; importUrl: string; rates: Record<string, string> }

function MappingsCard({ channel }: { channel: Channel }) {
  const qc = useQueryClient()
  const types = useAdminRoomTypes()
  const isICal = channel.channel === 'ICal'
  const [rows, setRows] = useState<Record<string, RoomRow>>(() => {
    const init: Record<string, RoomRow> = {}
    for (const m of channel.mappings) {
      const row = (init[m.roomTypeId] ??= { externalRoomId: m.externalRoomId, importUrl: '', rates: {} })
      if (m.ratePlanId) row.rates[m.ratePlanId] = m.externalRateId ?? ''
      else { row.externalRoomId = m.externalRoomId; row.importUrl = m.importUrl ?? '' }
    }
    return init
  })
  const update = (roomTypeId: string, patch: Partial<RoomRow>) =>
    setRows((r) => ({ ...r, [roomTypeId]: { externalRoomId: '', importUrl: '', rates: {}, ...r[roomTypeId], ...patch } }))

  const save = useMutation({
    mutationFn: () => {
      const list: ChannelMapping[] = []
      for (const [roomTypeId, row] of Object.entries(rows)) {
        if (!row.externalRoomId.trim()) continue
        list.push({ roomTypeId, ratePlanId: null, externalRoomId: row.externalRoomId, externalRateId: null, importUrl: row.importUrl || null })
        for (const [ratePlanId, ext] of Object.entries(row.rates))
          if (ext.trim()) list.push({ roomTypeId, ratePlanId, externalRoomId: row.externalRoomId, externalRateId: ext })
      }
      return put(`/api/admin/channels/${channel.id}/mappings`, list)
    },
    onSuccess: () => qc.invalidateQueries({ queryKey: ['admin', 'channel', channel.id] }),
  })

  return (
    <Card title="Oda & fiyat planı eşlemeleri">
      <p className="small muted">
        {isICal
          ? 'Her oda tipi için bir etiket (örn. ilan adı) ve karşı tarafın iCal dışa aktarma URL\'sini girin.'
          : 'Kanal panelindeki oda (room type) ve fiyat planı (rate plan) kimliklerini girin. Eşlenmeyen odalar kanala gönderilmez.'}
      </p>
      {types.isLoading && <Loading />}
      <div className="table-wrap">
        <table className="table">
          <thead><tr><th>Oda tipi / plan</th><th>{isICal ? 'Etiket' : 'Kanal oda ID'}</th><th>{isICal ? 'İçe aktarma URL (iCal)' : 'Kanal plan ID'}</th></tr></thead>
          <tbody>
            {types.data?.map((t) => (
              <Fragment key={t.id}>
                <tr>
                  <td><strong>{t.name}</strong></td>
                  <td><Input value={rows[t.id]?.externalRoomId ?? ''} onChange={(e) => update(t.id, { externalRoomId: e.target.value })} /></td>
                  <td>{isICal && <Input type="url" placeholder="https://www.airbnb.com/calendar/ical/….ics" value={rows[t.id]?.importUrl ?? ''}
                    onChange={(e) => update(t.id, { importUrl: e.target.value })} />}</td>
                </tr>
                {!isICal && t.ratePlans.map((p) => (
                  <tr key={p.id}>
                    <td className="small" style={{ paddingLeft: 24 }}>{p.name}</td>
                    <td />
                    <td><Input value={rows[t.id]?.rates[p.id] ?? ''} disabled={!rows[t.id]?.externalRoomId}
                      onChange={(e) => update(t.id, { rates: { ...rows[t.id]?.rates, [p.id]: e.target.value } })} /></td>
                  </tr>
                ))}
              </Fragment>
            ))}
          </tbody>
        </table>
      </div>
      <ErrorMessage error={save.error} />
      {save.isSuccess && <Alert tone="success">Eşlemeler kaydedildi. Tam senkronizasyon için "Tümünü gönder" kullanın.</Alert>}
      <div className="row" style={{ marginTop: 'var(--space-3)' }}>
        <Button variant="primary" onClick={() => save.mutate()} disabled={save.isPending}>Eşlemeleri kaydet</Button>
      </div>

      {isICal && !!channel.iCalExports?.length && (
        <div className="stack" style={{ marginTop: 'var(--space-4)' }}>
          <strong>Dışa aktarma URL'leri</strong>
          <p className="small muted" style={{ margin: 0 }}>Bu adresleri Airbnb / Vrbo "takvimi içe aktar" alanına yapıştırın. Dolu tarihler orada kapanır.</p>
          {channel.iCalExports.map((x) => (
            <Field key={x.roomTypeId} label={types.data?.find((t) => t.id === x.roomTypeId)?.name ?? x.roomTypeId}>
              <Input readOnly value={x.url} onFocus={(e) => e.target.select()} />
            </Field>
          ))}
        </div>
      )}
    </Card>
  )
}

function SyncCard({ channel, provider }: { channel: Channel; provider?: ChannelProvider }) {
  const qc = useQueryClient()
  const logs = useQuery({ queryKey: ['admin', 'channel-logs', channel.id], queryFn: () => get<ChannelSyncLog[]>(`/api/admin/channels/${channel.id}/logs?take=50`) })
  const done = () => { qc.invalidateQueries({ queryKey: ['admin', 'channel-logs', channel.id] }); qc.invalidateQueries({ queryKey: ['admin', 'channel', channel.id] }) }
  const test = useMutation({ mutationFn: () => post<{ success: boolean; message: string }>(`/api/admin/channels/${channel.id}/test`), onSettled: done })
  const pull = useMutation({ mutationFn: () => post<{ created: number; modified: number; cancelled: number; errors: string[] }>(`/api/admin/channels/${channel.id}/pull`), onSettled: done })
  const push = useMutation({ mutationFn: () => post(`/api/admin/channels/${channel.id}/push`), onSettled: done })

  return (
    <Card title="Senkronizasyon">
      <div className="row">
        <Button onClick={() => test.mutate()} disabled={test.isPending}>Bağlantıyı test et</Button>
        {provider?.supportsReservationPull && <Button onClick={() => pull.mutate()} disabled={pull.isPending}>Rezervasyonları şimdi çek</Button>}
        {provider?.supportsAriPush && <Button onClick={() => push.mutate()} disabled={push.isPending}>Tümünü gönder (365 gün)</Button>}
      </div>
      <div className="stack" style={{ marginTop: 'var(--space-3)' }}>
        {test.data && <Alert tone={test.data.success ? 'success' : 'error'}>{test.data.message}</Alert>}
        {pull.data && <Alert tone={pull.data.errors.length ? 'warning' : 'success'}>
          Yeni {pull.data.created}, değişen {pull.data.modified}, iptal {pull.data.cancelled}{pull.data.errors.length > 0 && ` · Hatalar: ${pull.data.errors.join('; ')}`}
        </Alert>}
        {push.isSuccess && <Alert tone="success">Gönderildi.</Alert>}
        <ErrorMessage error={test.error ?? pull.error ?? push.error} />
      </div>
      <h3 style={{ marginTop: 'var(--space-6)' }}>Son işlemler</h3>
      <div className="table-wrap">
        <table className="table small">
          <thead><tr><th>Zaman</th><th>Yön</th><th>İşlem</th><th>Sonuç</th><th>Adet</th><th>Mesaj</th><th className="right">ms</th></tr></thead>
          <tbody>
            {logs.data?.map((l) => (
              <tr key={l.id}>
                <td>{dateTime(l.createdAt)}</td>
                <td>{l.direction === 'Push' ? '→ Gönder' : '← Çek'}</td>
                <td>{l.operation}</td>
                <td>{l.success ? <Badge tone="success">OK</Badge> : <Badge tone="danger">Hata</Badge>}</td>
                <td>{l.itemCount}</td>
                <td style={{ maxWidth: 420, overflowWrap: 'anywhere' }}>{l.message}</td>
                <td className="right">{l.durationMs}</td>
              </tr>
            ))}
            {logs.data?.length === 0 && <tr><td colSpan={7} className="muted">Kayıt yok</td></tr>}
          </tbody>
        </table>
      </div>
    </Card>
  )
}
