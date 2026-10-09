import { createContext, useCallback, useContext, useMemo, useSyncExternalStore, type ReactNode } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { ApiError, get } from '@/api/client'
import type { Me } from '@/api/types'
import { tokenStore, type Tokens } from './tokenStore'

interface AuthState {
  isAuthenticated: boolean
  user: Me | undefined
  isLoading: boolean
  login: (email: string, password: string) => Promise<void>
  logout: () => void
  hasRole: (role: string) => boolean
}

const AuthContext = createContext<AuthState | null>(null)

export function AuthProvider({ children }: { children: ReactNode }) {
  const tokens = useSyncExternalStore(tokenStore.subscribe, () => tokenStore.get()?.accessToken ?? null)
  const queryClient = useQueryClient()
  const me = useQuery({ queryKey: ['me', tokens], queryFn: () => get<Me>('/api/auth/me'), enabled: !!tokens, retry: false })

  const login = useCallback(async (email: string, password: string) => {
    const res = await fetch('/api/auth/login', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ email, password }),
    })
    if (!res.ok) {
      const problem = await res.json().catch(() => ({}))
      throw new ApiError(res.status, problem.detail ?? problem.title ?? 'Giriş başarısız.')
    }
    tokenStore.set((await res.json()) as Tokens)
  }, [])

  const logout = useCallback(() => {
    tokenStore.clear()
    queryClient.clear()
  }, [queryClient])

  const value = useMemo<AuthState>(() => ({
    isAuthenticated: !!tokens,
    user: me.data,
    isLoading: !!tokens && me.isLoading,
    login,
    logout,
    hasRole: (role) => me.data?.roles.includes(role) ?? false,
  }), [tokens, me.data, me.isLoading, login, logout])

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>
}

// eslint-disable-next-line react-refresh/only-export-components
export function useAuth() {
  const ctx = useContext(AuthContext)
  if (!ctx) throw new Error('useAuth must be used inside AuthProvider')
  return ctx
}
