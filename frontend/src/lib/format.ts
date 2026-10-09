const locale = 'tr-TR'

export function money(amount: number, currency = 'TRY') {
  return new Intl.NumberFormat(locale, { style: 'currency', currency, maximumFractionDigits: 2 }).format(amount)
}

/** Parses yyyy-MM-dd as a local calendar date (no timezone shift). */
export function parseDate(iso: string) {
  const [y, m, d] = iso.slice(0, 10).split('-').map(Number)
  return new Date(y!, m! - 1, d!)
}

export function toIso(date: Date) {
  const y = date.getFullYear()
  const m = String(date.getMonth() + 1).padStart(2, '0')
  const d = String(date.getDate()).padStart(2, '0')
  return `${y}-${m}-${d}`
}

export function addDays(iso: string, days: number) {
  const d = parseDate(iso)
  d.setDate(d.getDate() + days)
  return toIso(d)
}

export const today = () => toIso(new Date())

export function nightsBetween(checkIn: string, checkOut: string) {
  return Math.round((parseDate(checkOut).getTime() - parseDate(checkIn).getTime()) / 86_400_000)
}

export function date(iso: string, opts: Intl.DateTimeFormatOptions = { day: '2-digit', month: 'short', year: 'numeric' }) {
  return new Intl.DateTimeFormat(locale, opts).format(parseDate(iso))
}

export function dateTime(iso?: string | null) {
  if (!iso) return '—'
  return new Intl.DateTimeFormat(locale, { dateStyle: 'short', timeStyle: 'short' }).format(new Date(iso))
}

export function weekday(iso: string) {
  return new Intl.DateTimeFormat(locale, { weekday: 'short' }).format(parseDate(iso))
}
