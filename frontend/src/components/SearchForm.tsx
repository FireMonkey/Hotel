import { useState, type FormEvent } from 'react'
import { useNavigate } from 'react-router'
import { addDays, today } from '@/lib/format'
import { Button, Field, Input, Select } from './ui'

export interface SearchValues { checkIn: string; checkOut: string; adults: number; children: number }

export function defaultSearch(): SearchValues {
  const checkIn = addDays(today(), 1)
  return { checkIn, checkOut: addDays(checkIn, 2), adults: 2, children: 0 }
}

export function SearchForm({ initial }: { initial?: Partial<SearchValues> }) {
  const navigate = useNavigate()
  const [v, setV] = useState<SearchValues>({ ...defaultSearch(), ...initial })

  const submit = (e: FormEvent) => {
    e.preventDefault()
    navigate(`/search?checkIn=${v.checkIn}&checkOut=${v.checkOut}&adults=${v.adults}&children=${v.children}`)
  }

  return (
    <form className="search-bar card" onSubmit={submit} aria-label="Müsaitlik ara">
      <Field label="Giriş">
        <Input type="date" required min={today()} value={v.checkIn}
          onChange={(e) => {
            const checkIn = e.target.value
            setV((s) => ({ ...s, checkIn, checkOut: s.checkOut <= checkIn ? addDays(checkIn, 1) : s.checkOut }))
          }} />
      </Field>
      <Field label="Çıkış">
        <Input type="date" required min={addDays(v.checkIn, 1)} value={v.checkOut}
          onChange={(e) => setV((s) => ({ ...s, checkOut: e.target.value }))} />
      </Field>
      <Field label="Yetişkin">
        <Select value={v.adults} onChange={(e) => setV((s) => ({ ...s, adults: Number(e.target.value) }))}>
          {[1, 2, 3, 4, 5, 6].map((n) => <option key={n} value={n}>{n}</option>)}
        </Select>
      </Field>
      <Field label="Çocuk">
        <Select value={v.children} onChange={(e) => setV((s) => ({ ...s, children: Number(e.target.value) }))}>
          {[0, 1, 2, 3, 4].map((n) => <option key={n} value={n}>{n}</option>)}
        </Select>
      </Field>
      <Button type="submit" variant="primary">Müsaitlik ara</Button>
    </form>
  )
}
