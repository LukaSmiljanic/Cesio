import { createContext } from 'react'

/** Button classes, also for links that look like buttons (design/prototype.html). */
export function buttonClass(
  variant: 'primary' | 'secondary' = 'primary',
  size: 'md' | 'sm' = 'md',
) {
  const look =
    variant === 'primary'
      ? 'border-accent bg-accent text-accent-ink hover:brightness-110'
      : 'border-line bg-surface text-ink hover:bg-surface-2'
  const height = size === 'sm' ? 'h-8 px-2.5 text-[13px]' : 'h-9 px-3.5 text-sm'
  return `inline-flex items-center justify-center gap-2 rounded-[7px] border font-semibold whitespace-nowrap disabled:cursor-not-allowed disabled:opacity-50 ${height} ${look}`
}

/** "2026-12-23" or an ISO time → "23.12.2026." */
export function formatDate(value: string | null | undefined) {
  if (!value) {
    return '—'
  }
  const [y, m, d] = value.slice(0, 10).split('-')
  return `${d}.${m}.${y}.`
}

/** Inside the app shell a page is a heading plus a panel; outside it (sign-in pages) a centred card. */
export const InShell = createContext(false)
