import { useQuery, useQueryClient } from '@tanstack/react-query'
import { useEffect, useRef, useState } from 'react'
import { useNavigate } from 'react-router'
import { notificationsApi, type NotificationItem } from '../api/notifications'
import { kindOf } from '../notifications/kinds'
import { KindIcon } from './NotificationIcon'

const pollMs = 20_000
const visibleMs = 8_000

/**
 * Notifications that arrive while the user is in the app appear as toasts (bottom right) and the screens refresh, so
 * a new offer or a deal step shows without reloading. Polling, no push channel: no new dependency.
 */
export function Toaster() {
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const seen = useRef<Set<string> | null>(null)
  const [toasts, setToasts] = useState<NotificationItem[]>([])
  const latest = useQuery({
    queryKey: ['notifications-latest'],
    queryFn: () => notificationsApi.list(),
    refetchInterval: pollMs,
  })

  useEffect(() => {
    const items = latest.data?.items
    if (!items) {
      return
    }
    // The first answer only marks what already existed; toasts are for what arrives later.
    if (seen.current === null) {
      seen.current = new Set(items.map((n) => n.id))
      return
    }
    const fresh = items.filter((n) => !n.readAt && !seen.current!.has(n.id))
    if (fresh.length === 0) {
      return
    }
    fresh.forEach((n) => seen.current!.add(n.id))
    setToasts((current) => [...fresh, ...current].slice(0, 3))
    // Whatever screen is open shows the new state (offers, deal steps, receivables).
    void queryClient.invalidateQueries({
      predicate: (query) => query.queryKey[0] !== 'notifications-latest',
    })
    const ids = fresh.map((n) => n.id)
    setTimeout(() => setToasts((current) => current.filter((t) => !ids.includes(t.id))), visibleMs)
  }, [latest.data, queryClient])

  async function open(n: NotificationItem) {
    setToasts((current) => current.filter((t) => t.id !== n.id))
    await notificationsApi.read(n.id).catch(() => undefined)
    await queryClient.invalidateQueries({ queryKey: ['notifications-unread'] })
    navigate(n.link)
  }

  if (toasts.length === 0) {
    return null
  }
  return (
    <div
      role="status"
      aria-live="polite"
      className="fixed right-4 bottom-4 z-[60] flex w-[min(380px,calc(100%-32px))] flex-col gap-2"
    >
      {toasts.map((n) => (
        <div
          key={n.id}
          className="flex items-center gap-3 rounded-lg border border-line bg-surface px-3.5 py-3 shadow-panel"
        >
          <KindIcon type={n.type} />
          <button type="button" onClick={() => void open(n)} className="min-w-0 flex-1 text-left">
            <span className="block text-sm font-semibold">{kindOf(n.type).label}</span>
            <span className="block truncate text-[12.5px] text-ink-3">
              {kindOf(n.type).group}
              {n.reference && ` · ${n.reference}`} · Otvori
            </span>
          </button>
          <button
            type="button"
            aria-label="Zatvori"
            onClick={() => setToasts((current) => current.filter((t) => t.id !== n.id))}
            className="grid size-7 flex-none place-items-center rounded-md text-ink-3 hover:bg-surface-2 hover:text-ink"
          >
            ✕
          </button>
        </div>
      ))}
    </div>
  )
}
