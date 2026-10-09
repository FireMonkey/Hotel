import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Link, useNavigate } from 'react-router'
import { get, post } from '@/api/client'
import type { Channel, ChannelProvider, ChannelType } from '@/api/types'
import { Alert, Badge, Button, Card, ErrorMessage, Loading, PageHeader } from '@/components/ui'
import { dateTime } from '@/lib/format'
import { channelLabel } from '@/lib/labels'

export default function ChannelsPage() {
  const navigate = useNavigate()
  const qc = useQueryClient()
  const channels = useQuery({ queryKey: ['admin', 'channels'], queryFn: () => get<Channel[]>('/api/admin/channels') })
  const providers = useQuery({ queryKey: ['admin', 'channel-providers'], queryFn: () => get<ChannelProvider[]>('/api/admin/channels/providers') })
  const [adding, setAdding] = useState(false)

  const create = useMutation({
    mutationFn: (p: ChannelProvider) => post<{ id: string }>('/api/admin/channels', {
      channel: p.type, name: p.displayName, isEnabled: false, settings: {}, commissionPercent: p.type === 'BookingCom' ? 15 : 0, rateMarkupPercent: 0,
    }),
    onSuccess: ({ id }) => { qc.invalidateQueries({ queryKey: ['admin', 'channels'] }); navigate(`/admin/channels/${id}`) },
  })

  return (
    <div className="stack">
      <PageHeader title="Kanallar (OTA entegrasyonları)" actions={<Button variant="primary" onClick={() => setAdding((a) => !a)}>+ Kanal bağla</Button>}>
        <p className="small muted" style={{ margin: 0 }}>
          Fiyat, müsaitlik ve kısıtlar bağlı kanallara otomatik gönderilir; kanallardaki rezervasyonlar periyodik olarak içeri alınır.
        </p>
      </PageHeader>

      {adding && (
        <div className="grid">
          {providers.data?.map((p) => (
            <Card key={p.type} title={p.displayName}>
              <div className="stack small">
                <span>{p.description}</span>
                <div className="row">
                  {p.supportsAriPush && <Badge tone="info">Fiyat/müsaitlik gönderir</Badge>}
                  {p.supportsReservationPull && <Badge tone="info">Rezervasyon alır</Badge>}
                  {p.requiresPartnerCertification ? <Badge tone="warning">Partner sertifikası gerekir</Badge> : <Badge tone="success">Hemen kullanılabilir</Badge>}
                </div>
                {p.documentationUrl && <a href={p.documentationUrl} target="_blank" rel="noreferrer">Dokümantasyon ↗</a>}
                <Button variant="primary" disabled={create.isPending} onClick={() => create.mutate(p)}>Bağla</Button>
              </div>
            </Card>
          ))}
          <ErrorMessage error={create.error} />
        </div>
      )}

      {channels.isLoading && <Loading />}
      <ErrorMessage error={channels.error} />
      {channels.data?.length === 0 && !adding && (
        <Alert tone="warning">
          Henüz kanal yok. Tek bir otel için en hızlı yol <strong>Channex</strong> gibi bir kanal yöneticisidir: Booking.com, Expedia, Airbnb ve Agoda'yı tek bağlantıyla yönetirsiniz.
          Doğrudan Booking.com/Expedia API'leri connectivity partner sertifikası gerektirir.
        </Alert>
      )}
      {!!channels.data?.length && (
        <table className="table">
          <thead><tr><th>Ad</th><th>Tür</th><th>Durum</th><th>Eşleme</th><th>Son rezervasyon çekme</th><th>Son fiyat gönderimi</th></tr></thead>
          <tbody>
            {channels.data.map((c) => (
              <tr key={c.id} className="is-clickable" onClick={() => navigate(`/admin/channels/${c.id}`)}>
                <td><Link to={`/admin/channels/${c.id}`}>{c.name}</Link></td>
                <td>{channelLabel[c.channel as ChannelType]}</td>
                <td>{!c.isEnabled ? <Badge>Kapalı</Badge> : c.lastError ? <Badge tone="danger" >Hata</Badge> : <Badge tone="success">Aktif</Badge>}</td>
                <td>{c.mappings.length}</td>
                <td className="small">{dateTime(c.lastReservationPullAt)}</td>
                <td className="small">{dateTime(c.lastAriPushAt)}</td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </div>
  )
}
