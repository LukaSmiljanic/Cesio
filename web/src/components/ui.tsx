// Minimal components in the shadcn/ui style (ADR-010): Tailwind classes, no extra runtime dependency.
import type { ButtonHTMLAttributes, InputHTMLAttributes, ReactNode } from 'react'
import { ApiError } from '../api/client'

export function Button({
  className = '',
  variant = 'primary',
  ...props
}: ButtonHTMLAttributes<HTMLButtonElement> & { variant?: 'primary' | 'secondary' }) {
  const look =
    variant === 'primary'
      ? 'bg-slate-900 text-white hover:bg-slate-700'
      : 'border border-slate-300 bg-white text-slate-900 hover:bg-slate-100'
  return (
    <button
      className={`inline-flex h-10 items-center justify-center rounded-md px-4 text-sm font-medium disabled:opacity-50 ${look} ${className}`}
      {...props}
    />
  )
}

export function Field({
  label,
  id,
  ...props
}: InputHTMLAttributes<HTMLInputElement> & { label: string; id: string }) {
  return (
    <div className="space-y-1">
      <label htmlFor={id} className="block text-sm font-medium">
        {label}
      </label>
      <input
        id={id}
        className="h-10 w-full rounded-md border border-slate-300 bg-white px-3 text-sm focus:outline-2 focus:outline-slate-400"
        {...props}
      />
    </div>
  )
}

export function Card({ title, children }: { title: string; children: ReactNode }) {
  return (
    <section className="mx-auto mt-16 w-full max-w-md rounded-lg border border-slate-200 bg-white p-6 shadow-sm">
      <h1 className="mb-4 text-xl font-semibold">{title}</h1>
      {children}
    </section>
  )
}

const messages: Record<string, string> = {
  invalid_credentials: 'Pogrešan email ili lozinka, ili je nalog privremeno zaključan.',
  invalid_code: 'Kod nije ispravan ili je istekao.',
  invalid_token: 'Link nije ispravan ili je istekao.',
  email_not_confirmed: 'Prvo potvrdite email adresu.',
  validation_failed: 'Podaci nisu ispravni.',
  PasswordBreached: 'Ova lozinka se pojavljuje u poznatim curenjima podataka. Izaberite drugu.',
  PasswordTooShort: 'Lozinka mora imati najmanje 12 znakova.',
}

export function ErrorMessage({ error }: { error: unknown }) {
  if (!error) {
    return null
  }
  const lines =
    error instanceof ApiError
      ? [
          messages[error.errorCode ?? ''] ?? error.message,
          ...error.errors.map((e) => messages[e.code] ?? e.message),
        ]
      : ['Došlo je do greške. Pokušajte ponovo.']
  return (
    <div
      role="alert"
      className="rounded-md border border-red-200 bg-red-50 p-3 text-sm text-red-800"
    >
      {[...new Set(lines)].map((line) => (
        <p key={line}>{line}</p>
      ))}
    </div>
  )
}
