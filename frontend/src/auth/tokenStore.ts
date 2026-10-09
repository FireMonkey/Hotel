export interface Tokens {
  accessToken: string
  refreshToken: string
  expiresIn: number
}

const KEY = 'hotel.auth'
const listeners = new Set<() => void>()

function read(): Tokens | null {
  try {
    const raw = localStorage.getItem(KEY)
    return raw ? (JSON.parse(raw) as Tokens) : null
  } catch {
    return null
  }
}

export const tokenStore = {
  get: read,
  set(tokens: Tokens) {
    try { localStorage.setItem(KEY, JSON.stringify(tokens)) } catch { /* storage unavailable */ }
    listeners.forEach((l) => l())
  },
  clear() {
    try { localStorage.removeItem(KEY) } catch { /* storage unavailable */ }
    listeners.forEach((l) => l())
  },
  subscribe(listener: () => void) {
    listeners.add(listener)
    return () => { listeners.delete(listener) }
  },
}
