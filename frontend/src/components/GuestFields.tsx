import type { Guest } from '@/api/types'
import { Field, Input } from './ui'

export function GuestFields({ value, onChange }: { value: Guest; onChange: (g: Guest) => void }) {
  const set = (k: keyof Guest) => (e: React.ChangeEvent<HTMLInputElement>) => onChange({ ...value, [k]: e.target.value })
  return (
    <div className="form-grid">
      <Field label="Ad"><Input required autoComplete="given-name" value={value.firstName} onChange={set('firstName')} /></Field>
      <Field label="Soyad"><Input required autoComplete="family-name" value={value.lastName} onChange={set('lastName')} /></Field>
      <Field label="E-posta"><Input required type="email" autoComplete="email" value={value.email} onChange={set('email')} /></Field>
      <Field label="Telefon"><Input type="tel" autoComplete="tel" value={value.phone ?? ''} onChange={set('phone')} /></Field>
      <Field label="Ülke (ISO kodu)" hint="Örn. TR, DE, GB">
        <Input maxLength={2} autoComplete="country" value={value.country ?? ''}
          onChange={(e) => onChange({ ...value, country: e.target.value.toUpperCase() })} />
      </Field>
    </div>
  )
}

export const emptyGuest = (): Guest => ({ firstName: '', lastName: '', email: '', phone: '', country: '' })
