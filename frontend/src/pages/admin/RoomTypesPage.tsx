import { useState } from 'react'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { del, post, put } from '@/api/client'
import type { AdminRoomType, MealPlan, RatePlan } from '@/api/types'
import { useAdminRoomTypes } from '@/api/hooks'
import { Badge, Button, Card, Checkbox, ErrorMessage, Field, Input, Loading, Modal, PageHeader, Select, Textarea } from '@/components/ui'
import { money } from '@/lib/format'
import { mealPlanLabel } from '@/lib/labels'

type TypeForm = Omit<AdminRoomType, 'id' | 'roomCount' | 'ratePlans' | 'amenities' | 'imageUrls'> & { id?: string; amenities: string; imageUrls: string }
type PlanForm = Omit<RatePlan, 'id'> & { id?: string; roomTypeId: string }

const emptyType = (): TypeForm => ({
  code: '', name: '', description: '', maxAdults: 2, maxChildren: 0, sizeSquareMeters: 20, basePrice: 1000, currency: 'TRY',
  amenities: '', imageUrls: '', isActive: true, sortOrder: 0,
})

export default function RoomTypesPage() {
  const qc = useQueryClient()
  const types = useAdminRoomTypes()
  const [typeForm, setTypeForm] = useState<TypeForm | null>(null)
  const [planForm, setPlanForm] = useState<PlanForm | null>(null)
  const refresh = () => qc.invalidateQueries({ queryKey: ['admin'] })

  const saveType = useMutation({
    mutationFn: (f: TypeForm) => {
      const body = { ...f, amenities: split(f.amenities), imageUrls: f.imageUrls.split("\n").map((x) => x.trim()).filter(Boolean) }
      return f.id ? put(`/api/admin/room-types/${f.id}`, body) : post('/api/admin/room-types', body)
    },
    onSuccess: () => { refresh(); setTypeForm(null) },
  })
  const savePlan = useMutation({
    mutationFn: (f: PlanForm) => (f.id ? put(`/api/admin/rate-plans/${f.id}`, f) : post(`/api/admin/room-types/${f.roomTypeId}/rate-plans`, f)),
    onSuccess: () => { refresh(); setPlanForm(null) },
  })
  const removeType = useMutation({ mutationFn: (id: string) => del(`/api/admin/room-types/${id}`), onSuccess: refresh })
  const removePlan = useMutation({ mutationFn: (id: string) => del(`/api/admin/rate-plans/${id}`), onSuccess: refresh })

  return (
    <div className="stack">
      <PageHeader title="Oda tipleri & fiyat planları" actions={<Button variant="primary" onClick={() => setTypeForm(emptyType())}>+ Oda tipi</Button>} />
      {types.isLoading && <Loading />}
      <ErrorMessage error={types.error ?? removeType.error ?? removePlan.error} />

      {types.data?.map((t) => (
        <Card key={t.id} title={<span className="row">{t.name} <span className="muted small">{t.code}</span> {!t.isActive && <Badge tone="danger">Pasif</Badge>}</span>}
          actions={<>
            <Button size="sm" onClick={() => setTypeForm({ ...t, amenities: t.amenities.join(', '), imageUrls: t.imageUrls.join('\n') })}>Düzenle</Button>
            <Button size="sm" variant="danger" onClick={() => confirm(`${t.name} silinsin mi?`) && removeType.mutate(t.id)}>Sil</Button>
          </>}>
          <p className="small muted">
            {t.roomCount} oda · {t.maxAdults} yetişkin + {t.maxChildren} çocuk · {t.sizeSquareMeters} m² · taban fiyat {money(t.basePrice, t.currency)}
          </p>
          <table className="table">
            <thead><tr><th>Plan</th><th>Pansiyon</th><th>İptal</th><th>Katsayı</th><th>Varsayılan gecelik</th><th /></tr></thead>
            <tbody>
              {t.ratePlans.map((p) => (
                <tr key={p.id}>
                  <td>{p.name} <span className="muted small">{p.code}</span> {!p.isActive && <Badge>Pasif</Badge>}</td>
                  <td>{mealPlanLabel[p.mealPlan]}</td>
                  <td>{p.isRefundable ? `${p.freeCancellationDays ?? 0} gün öncesine kadar ücretsiz` : 'İade edilemez'}</td>
                  <td>×{p.priceFactor}</td>
                  <td>{money(t.basePrice * p.priceFactor, t.currency)}</td>
                  <td className="right">
                    <div className="row" style={{ justifyContent: 'flex-end' }}>
                      <Button size="sm" onClick={() => setPlanForm({ ...p, roomTypeId: t.id })}>Düzenle</Button>
                      <Button size="sm" variant="danger" onClick={() => confirm(`${p.name} silinsin mi?`) && removePlan.mutate(p.id)}>Sil</Button>
                    </div>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
          <Button size="sm" onClick={() => setPlanForm({ roomTypeId: t.id, code: '', name: '', mealPlan: 'BedAndBreakfast', isRefundable: true, freeCancellationDays: 1, priceFactor: 1, isActive: true })}>
            + Fiyat planı
          </Button>
        </Card>
      ))}

      <Modal open={!!typeForm} onClose={() => setTypeForm(null)} title={typeForm?.id ? 'Oda tipini düzenle' : 'Yeni oda tipi'}>
        {typeForm && (
          <form className="stack" onSubmit={(e) => { e.preventDefault(); saveType.mutate(typeForm) }}>
            <div className="form-grid">
              <Field label="Kod"><Input required maxLength={32} value={typeForm.code} onChange={(e) => setTypeForm({ ...typeForm, code: e.target.value })} /></Field>
              <Field label="Ad"><Input required value={typeForm.name} onChange={(e) => setTypeForm({ ...typeForm, name: e.target.value })} /></Field>
              <Field label="Max yetişkin"><Input type="number" min={1} value={typeForm.maxAdults} onChange={(e) => setTypeForm({ ...typeForm, maxAdults: Number(e.target.value) })} /></Field>
              <Field label="Max çocuk"><Input type="number" min={0} value={typeForm.maxChildren} onChange={(e) => setTypeForm({ ...typeForm, maxChildren: Number(e.target.value) })} /></Field>
              <Field label="m²"><Input type="number" min={0} value={typeForm.sizeSquareMeters} onChange={(e) => setTypeForm({ ...typeForm, sizeSquareMeters: Number(e.target.value) })} /></Field>
              <Field label="Taban gecelik fiyat"><Input type="number" min={1} step="0.01" value={typeForm.basePrice} onChange={(e) => setTypeForm({ ...typeForm, basePrice: Number(e.target.value) })} /></Field>
              <Field label="Para birimi"><Input maxLength={3} value={typeForm.currency} onChange={(e) => setTypeForm({ ...typeForm, currency: e.target.value.toUpperCase() })} /></Field>
              <Field label="Sıra"><Input type="number" value={typeForm.sortOrder} onChange={(e) => setTypeForm({ ...typeForm, sortOrder: Number(e.target.value) })} /></Field>
            </div>
            <Field label="Açıklama"><Textarea value={typeForm.description ?? ''} onChange={(e) => setTypeForm({ ...typeForm, description: e.target.value })} /></Field>
            <Field label="Olanaklar" hint="Virgülle ayırın"><Input value={typeForm.amenities} onChange={(e) => setTypeForm({ ...typeForm, amenities: e.target.value })} /></Field>
            <Field label="Görsel URL'leri" hint="Her satıra bir URL"><Textarea value={typeForm.imageUrls} onChange={(e) => setTypeForm({ ...typeForm, imageUrls: e.target.value })} /></Field>
            <Checkbox label="Aktif (satışta)" checked={typeForm.isActive} onChange={(e) => setTypeForm({ ...typeForm, isActive: e.target.checked })} />
            <ErrorMessage error={saveType.error} />
            <Button type="submit" variant="primary" disabled={saveType.isPending}>Kaydet</Button>
          </form>
        )}
      </Modal>

      <Modal open={!!planForm} onClose={() => setPlanForm(null)} title={planForm?.id ? 'Fiyat planını düzenle' : 'Yeni fiyat planı'}>
        {planForm && (
          <form className="stack" onSubmit={(e) => { e.preventDefault(); savePlan.mutate(planForm) }}>
            <div className="form-grid">
              <Field label="Kod"><Input required maxLength={32} value={planForm.code} onChange={(e) => setPlanForm({ ...planForm, code: e.target.value })} /></Field>
              <Field label="Ad"><Input required value={planForm.name} onChange={(e) => setPlanForm({ ...planForm, name: e.target.value })} /></Field>
              <Field label="Pansiyon">
                <Select value={planForm.mealPlan} onChange={(e) => setPlanForm({ ...planForm, mealPlan: e.target.value as MealPlan })}>
                  {(Object.keys(mealPlanLabel) as MealPlan[]).map((m) => <option key={m} value={m}>{mealPlanLabel[m]}</option>)}
                </Select>
              </Field>
              <Field label="Fiyat katsayısı" hint="Taban fiyata uygulanır (örn. 0.85 = %15 indirim)">
                <Input type="number" step="0.01" min={0.01} value={planForm.priceFactor} onChange={(e) => setPlanForm({ ...planForm, priceFactor: Number(e.target.value) })} />
              </Field>
              {planForm.isRefundable && (
                <Field label="Ücretsiz iptal (gün)"><Input type="number" min={0} value={planForm.freeCancellationDays ?? 0}
                  onChange={(e) => setPlanForm({ ...planForm, freeCancellationDays: Number(e.target.value) })} /></Field>
              )}
            </div>
            <Checkbox label="İade edilebilir" checked={planForm.isRefundable} onChange={(e) => setPlanForm({ ...planForm, isRefundable: e.target.checked })} />
            <Checkbox label="Aktif" checked={planForm.isActive} onChange={(e) => setPlanForm({ ...planForm, isActive: e.target.checked })} />
            <ErrorMessage error={savePlan.error} />
            <Button type="submit" variant="primary" disabled={savePlan.isPending}>Kaydet</Button>
          </form>
        )}
      </Modal>
    </div>
  )
}

const split = (s: string) => s.split(/[\n,]/).map((x) => x.trim()).filter(Boolean)
