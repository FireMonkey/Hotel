import { useQuery } from '@tanstack/react-query'
import { useNavigate, useSearchParams } from 'react-router'
import { get, qs } from '@/api/client'
import type { RoomTypeAvailability } from '@/api/types'
import { defaultSearch, SearchForm } from '@/components/SearchForm'
import { RoomCard } from '@/components/RoomCard'
import { Alert, Badge, Button, ErrorMessage, Loading } from '@/components/ui'
import { date, money, nightsBetween } from '@/lib/format'
import { mealPlanLabel } from '@/lib/labels'

export default function SearchPage() {
  const [params] = useSearchParams()
  const navigate = useNavigate()
  const d = defaultSearch()
  const search = {
    checkIn: params.get('checkIn') ?? d.checkIn,
    checkOut: params.get('checkOut') ?? d.checkOut,
    adults: Number(params.get('adults') ?? d.adults),
    children: Number(params.get('children') ?? d.children),
  }
  const nights = nightsBetween(search.checkIn, search.checkOut)

  const result = useQuery({
    queryKey: ['availability', search],
    queryFn: () => get<RoomTypeAvailability[]>(`/api/public/availability${qs(search)}`),
    enabled: params.has('checkIn'),
  })

  return (
    <div className="stack">
      <SearchForm initial={search} />
      {params.has('checkIn') && (
        <p className="muted">{date(search.checkIn)} – {date(search.checkOut)} · {nights} gece · {search.adults} yetişkin{search.children > 0 && `, ${search.children} çocuk`}</p>
      )}
      {result.isLoading && <Loading label="Müsaitlik kontrol ediliyor…" />}
      <ErrorMessage error={result.error} />
      {result.data?.length === 0 && <Alert tone="warning">Seçilen tarihlerde uygun oda bulunamadı. Lütfen farklı tarihler deneyin.</Alert>}

      <div className="stack">
        {result.data?.map((room) => (
          <RoomCard key={room.roomTypeId} room={room} footer={
            <div className="stack" style={{ gap: 'var(--space-2)' }}>
              {room.availableRooms <= 3 && <Badge tone="warning">Son {room.availableRooms} oda!</Badge>}
              {room.rates.map((rate) => (
                <div key={rate.ratePlanId} className="row row--between card" style={{ boxShadow: 'none' }}>
                  <div>
                    <strong>{rate.name}</strong>
                    <div className="small muted">
                      {mealPlanLabel[rate.mealPlan]} · {rate.isRefundable && rate.freeCancellationDays != null
                        ? `Girişten ${rate.freeCancellationDays} gün öncesine kadar ücretsiz iptal`
                        : 'İade edilemez'}
                    </div>
                  </div>
                  <div className="row">
                    <div className="right">
                      <strong>{money(rate.totalPrice, rate.currency)}</strong>
                      <div className="small muted">{nights} gece · ort. {money(rate.averageNightlyPrice, rate.currency)}</div>
                    </div>
                    <Button variant="primary" onClick={() => navigate(`/book${qs({ ...search, roomTypeId: room.roomTypeId, ratePlanId: rate.ratePlanId })}`)}>
                      Seç
                    </Button>
                  </div>
                </div>
              ))}
            </div>
          } />
        ))}
      </div>
    </div>
  )
}
