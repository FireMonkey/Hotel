import { tokenStore } from '@/auth/tokenStore'

export class ApiError extends Error {
  status: number
  code?: string
  constructor(status: number, message: string, code?: string) {
    super(message)
    this.status = status
    this.code = code
  }
}

type Method = 'GET' | 'POST' | 'PUT' | 'PATCH' | 'DELETE'

let refreshing: Promise<boolean> | null = null

async function refreshToken(): Promise<boolean> {
  const refresh = tokenStore.get()?.refreshToken
  if (!refresh) return false
  refreshing ??= fetch('/api/auth/refresh', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ refreshToken: refresh }),
  })
    .then(async (res) => {
      if (!res.ok) return false
      tokenStore.set(await res.json())
      return true
    })
    .catch(() => false)
    .finally(() => { refreshing = null })
  return refreshing
}

export async function api<T>(method: Method, path: string, body?: unknown, retry = true): Promise<T> {
  const headers: Record<string, string> = {}
  if (body !== undefined) headers['Content-Type'] = 'application/json'
  const token = tokenStore.get()?.accessToken
  if (token) headers.Authorization = `Bearer ${token}`

  const res = await fetch(path, { method, headers, body: body === undefined ? undefined : JSON.stringify(body) })

  if (res.status === 401 && retry && tokenStore.get()) {
    if (await refreshToken()) return api<T>(method, path, body, false)
    tokenStore.clear()
  }
  if (!res.ok) {
    let message = res.statusText
    let code: string | undefined
    try {
      const problem = await res.json()
      message = problem.title ?? problem.detail ?? message
      code = problem.code
      if (problem.errors) message = Object.values(problem.errors as Record<string, string[]>).flat().join(' ')
    } catch { /* not JSON */ }
    if (res.status === 429) message = 'Too many requests. Please wait a moment.'
    throw new ApiError(res.status, message, code)
  }
  if (res.status === 204 || res.headers.get('content-length') === '0') return undefined as T
  const text = await res.text()
  return (text ? JSON.parse(text) : undefined) as T
}

export const get = <T>(path: string) => api<T>('GET', path)
export const post = <T>(path: string, body?: unknown) => api<T>('POST', path, body ?? {})
export const put = <T>(path: string, body: unknown) => api<T>('PUT', path, body)
export const patch = <T>(path: string, body: unknown) => api<T>('PATCH', path, body)
export const del = <T>(path: string) => api<T>('DELETE', path)

export function qs(params: object): string {
  const p = new URLSearchParams()
  for (const [k, v] of Object.entries(params) as [string, unknown][]) if (v !== undefined && v !== null && v !== '') p.set(k, String(v))
  const s = p.toString()
  return s ? `?${s}` : ''
}
