import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { del, get, post } from '@/api/client'
import type { AdminUser } from '@/api/types'
import { Badge, Button, Card, ErrorMessage, Field, Input, Loading, PageHeader, Select } from '@/components/ui'

export default function UsersPage() {
  const qc = useQueryClient()
  const users = useQuery({ queryKey: ['admin', 'users'], queryFn: () => get<AdminUser[]>('/api/admin/users') })
  const [form, setForm] = useState({ email: '', fullName: '', password: '', role: 'Staff' })
  const create = useMutation({
    mutationFn: () => post('/api/admin/users', form),
    onSuccess: () => { qc.invalidateQueries({ queryKey: ['admin', 'users'] }); setForm({ email: '', fullName: '', password: '', role: 'Staff' }) },
  })
  const remove = useMutation({ mutationFn: (id: string) => del(`/api/admin/users/${id}`), onSuccess: () => qc.invalidateQueries({ queryKey: ['admin', 'users'] }) })

  return (
    <div className="stack">
      <PageHeader title="Kullanıcılar" />
      {users.isLoading && <Loading />}
      <ErrorMessage error={users.error ?? remove.error} />
      <table className="table">
        <thead><tr><th>E-posta</th><th>Ad</th><th>Rol</th><th /></tr></thead>
        <tbody>
          {users.data?.map((u) => (
            <tr key={u.id}>
              <td>{u.email} {u.locked && <Badge tone="danger">Kilitli</Badge>}</td>
              <td>{u.fullName}</td>
              <td>{u.roles.join(', ')}</td>
              <td className="right"><Button size="sm" variant="danger" onClick={() => confirm(`${u.email} silinsin mi?`) && remove.mutate(u.id)}>Sil</Button></td>
            </tr>
          ))}
        </tbody>
      </table>
      <Card title="Yeni kullanıcı">
        <form className="form-grid" style={{ alignItems: 'end' }} onSubmit={(e) => { e.preventDefault(); create.mutate() }}>
          <Field label="E-posta"><Input type="email" required value={form.email} onChange={(e) => setForm({ ...form, email: e.target.value })} /></Field>
          <Field label="Ad soyad"><Input required value={form.fullName} onChange={(e) => setForm({ ...form, fullName: e.target.value })} /></Field>
          <Field label="Şifre" hint="En az 10 karakter"><Input type="password" required minLength={10} autoComplete="new-password" value={form.password} onChange={(e) => setForm({ ...form, password: e.target.value })} /></Field>
          <Field label="Rol">
            <Select value={form.role} onChange={(e) => setForm({ ...form, role: e.target.value })}>
              <option value="Staff">Personel</option>
              <option value="Admin">Yönetici</option>
            </Select>
          </Field>
          <Button type="submit" variant="primary" disabled={create.isPending}>Ekle</Button>
        </form>
        <ErrorMessage error={create.error} />
      </Card>
    </div>
  )
}
