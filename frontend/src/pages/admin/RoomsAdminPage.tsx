import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Link } from 'react-router'
import { del, get, patch, post, put } from '@/api/client'
import type { AdminRoom, HousekeepingStatus, RoomStatus } from '@/api/types'
import { useAdminRoomTypes } from '@/api/hooks'
import { Badge, Button, ErrorMessage, Field, Input, Loading, Modal, PageHeader, Select } from '@/components/ui'
import { date } from '@/lib/format'
import { housekeepingLabel, roomStatusLabel } from '@/lib/labels'

interface RoomForm { id?: string; number: string; floor: number; roomTypeId: string; status: RoomStatus; notes: string }

export default function RoomsAdminPage() {
  const qc = useQueryClient()
  const rooms = useQuery({ queryKey: ['admin', 'rooms'], queryFn: () => get<AdminRoom[]>('/api/admin/rooms') })
  const types = useAdminRoomTypes()
  const [edit, setEdit] = useState<RoomForm | null>(null)
  const refresh = () => qc.invalidateQueries({ queryKey: ['admin'] })

  const save = useMutation({
    mutationFn: (f: RoomForm) => (f.id ? put(`/api/admin/rooms/${f.id}`, f) : post('/api/admin/rooms', f)),
    onSuccess: () => { refresh(); setEdit(null) },
  })
  const remove = useMutation({ mutationFn: (id: string) => del(`/api/admin/rooms/${id}`), onSuccess: refresh })
  const hk = useMutation({
    mutationFn: (v: { id: string; housekeeping: HousekeepingStatus }) => patch(`/api/admin/rooms/${v.id}/housekeeping`, { housekeeping: v.housekeeping }),
    onSuccess: refresh,
  })

  return (
    <div className="stack">
      <PageHeader title="Odalar" actions={
        <Button variant="primary" disabled={!types.data?.length}
          onClick={() => setEdit({ number: '', floor: 1, roomTypeId: types.data?.[0]?.id ?? '', status: 'Available', notes: '' })}>+ Oda ekle</Button>
      } />
      {rooms.isLoading && <Loading />}
      <ErrorMessage error={rooms.error ?? remove.error ?? hk.error} />
      <div className="table-wrap">
        <table className="table">
          <thead><tr><th>Oda</th><th>Kat</th><th>Tip</th><th>Durum</th><th>Temizlik</th><th>Misafir</th><th /></tr></thead>
          <tbody>
            {rooms.data?.map((r) => (
              <tr key={r.id}>
                <td><strong>{r.number}</strong></td>
                <td>{r.floor}</td>
                <td>{r.roomTypeName}</td>
                <td><Badge tone={r.status === 'Available' ? 'success' : 'danger'}>{roomStatusLabel[r.status]}</Badge></td>
                <td>
                  <select className="select" style={{ width: 'auto' }} value={r.housekeeping}
                    onChange={(e) => hk.mutate({ id: r.id, housekeeping: e.target.value as HousekeepingStatus })}>
                    {(Object.keys(housekeepingLabel) as HousekeepingStatus[]).map((h) => <option key={h} value={h}>{housekeepingLabel[h]}</option>)}
                  </select>
                </td>
                <td className="small">{r.currentBooking
                  ? <Link to={`/admin/bookings/${r.currentBooking.id}`}>{r.currentBooking.guestName} (→ {date(r.currentBooking.checkOut)})</Link>
                  : <span className="muted">Boş</span>}</td>
                <td className="right">
                  <div className="row" style={{ justifyContent: 'flex-end' }}>
                    <Button size="sm" onClick={() => setEdit({ id: r.id, number: r.number, floor: r.floor, roomTypeId: r.roomTypeId, status: r.status, notes: r.notes ?? '' })}>Düzenle</Button>
                    <Button size="sm" variant="danger" onClick={() => confirm(`Oda ${r.number} silinsin mi?`) && remove.mutate(r.id)}>Sil</Button>
                  </div>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>

      <Modal open={!!edit} onClose={() => setEdit(null)} title={edit?.id ? 'Oda düzenle' : 'Yeni oda'}>
        {edit && (
          <form className="stack" onSubmit={(e) => { e.preventDefault(); save.mutate(edit) }}>
            <div className="form-grid">
              <Field label="Oda no"><Input required value={edit.number} onChange={(e) => setEdit({ ...edit, number: e.target.value })} /></Field>
              <Field label="Kat"><Input type="number" value={edit.floor} onChange={(e) => setEdit({ ...edit, floor: Number(e.target.value) })} /></Field>
              <Field label="Oda tipi">
                <Select value={edit.roomTypeId} onChange={(e) => setEdit({ ...edit, roomTypeId: e.target.value })}>
                  {types.data?.map((t) => <option key={t.id} value={t.id}>{t.name}</option>)}
                </Select>
              </Field>
              <Field label="Durum" hint="Satışta olmayan odalar müsaitlikten düşer">
                <Select value={edit.status} onChange={(e) => setEdit({ ...edit, status: e.target.value as RoomStatus })}>
                  {(Object.keys(roomStatusLabel) as RoomStatus[]).map((s) => <option key={s} value={s}>{roomStatusLabel[s]}</option>)}
                </Select>
              </Field>
            </div>
            <Field label="Not"><Input value={edit.notes} onChange={(e) => setEdit({ ...edit, notes: e.target.value })} /></Field>
            <ErrorMessage error={save.error} />
            <Button type="submit" variant="primary" disabled={save.isPending}>Kaydet</Button>
          </form>
        )}
      </Modal>
    </div>
  )
}
