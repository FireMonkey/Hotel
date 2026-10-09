import type { ButtonHTMLAttributes, InputHTMLAttributes, ReactNode, SelectHTMLAttributes, TextareaHTMLAttributes } from 'react'
import { useEffect, useRef } from 'react'

// Presentational primitives. Restyle via styles/*.css; keep the props stable.

type ButtonProps = ButtonHTMLAttributes<HTMLButtonElement> & { variant?: 'default' | 'primary' | 'danger'; size?: 'md' | 'sm' }

export function Button({ variant = 'default', size = 'md', className = '', type = 'button', ...rest }: ButtonProps) {
  const cls = ['btn', variant !== 'default' && `btn--${variant}`, size === 'sm' && 'btn--sm', className].filter(Boolean).join(' ')
  return <button type={type} className={cls} {...rest} />
}

export function Card({ title, actions, children, className = '' }: { title?: ReactNode; actions?: ReactNode; children: ReactNode; className?: string }) {
  return (
    <section className={`card ${className}`}>
      {(title || actions) && (
        <div className="row row--between card__title">
          <div>{title}</div>
          {actions && <div className="row">{actions}</div>}
        </div>
      )}
      {children}
    </section>
  )
}

export function Field({ label, hint, children }: { label: ReactNode; hint?: ReactNode; children: ReactNode }) {
  return (
    <label className="field">
      <span className="field__label">{label}</span>
      {children}
      {hint && <span className="field__hint">{hint}</span>}
    </label>
  )
}

export const Input = (props: InputHTMLAttributes<HTMLInputElement>) => <input className="input" {...props} />
export const Select = (props: SelectHTMLAttributes<HTMLSelectElement>) => <select className="select" {...props} />
export const Textarea = (props: TextareaHTMLAttributes<HTMLTextAreaElement>) => <textarea className="textarea" rows={3} {...props} />

export function Checkbox({ label, ...props }: InputHTMLAttributes<HTMLInputElement> & { label: ReactNode }) {
  return (
    <label className="checkbox">
      <input type="checkbox" {...props} />
      <span>{label}</span>
    </label>
  )
}

export function Badge({ tone = 'default', children }: { tone?: 'default' | 'success' | 'warning' | 'danger' | 'info'; children: ReactNode }) {
  return <span className={`badge ${tone !== 'default' ? `badge--${tone}` : ''}`}>{children}</span>
}

export function Alert({ tone = 'error', children }: { tone?: 'error' | 'success' | 'warning'; children: ReactNode }) {
  return <div role={tone === 'error' ? 'alert' : 'status'} className={`alert alert--${tone}`}>{children}</div>
}

export function ErrorMessage({ error }: { error: unknown }) {
  if (!error) return null
  return <Alert>{error instanceof Error ? error.message : 'Bir hata oluştu.'}</Alert>
}

export function Loading({ label = 'Yükleniyor…' }: { label?: string }) {
  return <p className="muted" aria-busy="true">{label}</p>
}

export function PageHeader({ title, actions, children }: { title: ReactNode; actions?: ReactNode; children?: ReactNode }) {
  return (
    <header className="stack" style={{ marginBottom: 'var(--space-6)', gap: 'var(--space-2)' }}>
      <div className="row row--between">
        <h1 style={{ margin: 0 }}>{title}</h1>
        {actions && <div className="row">{actions}</div>}
      </div>
      {children}
    </header>
  )
}

export function Modal({ open, onClose, title, children }: { open: boolean; onClose: () => void; title: ReactNode; children: ReactNode }) {
  const ref = useRef<HTMLDialogElement>(null)
  useEffect(() => {
    const d = ref.current
    if (!d) return
    if (open && !d.open) d.showModal()
    if (!open && d.open) d.close()
  }, [open])
  return (
    <dialog ref={ref} onClose={onClose}>
      <div className="row row--between" style={{ marginBottom: 'var(--space-4)' }}>
        <h2 style={{ margin: 0 }}>{title}</h2>
        <Button size="sm" onClick={onClose} aria-label="Kapat">✕</Button>
      </div>
      {open && children}
    </dialog>
  )
}

export function Pagination({ page, pageSize, total, onPage }: { page: number; pageSize: number; total: number; onPage: (p: number) => void }) {
  const pages = Math.max(1, Math.ceil(total / pageSize))
  return (
    <div className="row" style={{ marginTop: 'var(--space-3)' }}>
      <Button size="sm" disabled={page <= 1} onClick={() => onPage(page - 1)}>‹ Önceki</Button>
      <span className="small muted">{page} / {pages} · {total} kayıt</span>
      <Button size="sm" disabled={page >= pages} onClick={() => onPage(page + 1)}>Sonraki ›</Button>
    </div>
  )
}
