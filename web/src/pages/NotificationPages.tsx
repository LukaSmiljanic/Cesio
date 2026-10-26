import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { Link, useNavigate } from 'react-router'
import { notificationsApi, type FactorAppetite } from '../api/notifications'
import { Segmented, Switch } from '../components/inputs'
import { KindIcon } from '../components/NotificationIcon'
import { kindOf } from '../notifications/kinds'
import { buttonClass } from '../components/styles'
import { Button, ErrorMessage, Field, PageHeader, Panel } from '../components/ui'

/** "Danas", "Juče" or the date, for grouping by day. */
function dayLabel(iso: string) {
  const day = new Date(iso)
  const today = new Date()
  const start = (d: Date) => new Date(d.getFullYear(), d.getMonth(), d.getDate()).getTime()
  const diff = Math.round((start(today) - start(day)) / 86_400_000)
  if (diff === 0) return 'Danas'
  if (diff === 1) return 'Juče'
  return day.toLocaleDateString('sr-Latn-RS', { weekday: 'long', day: 'numeric', month: 'long' })
}

const time = (iso: string) =>
  new Date(iso).toLocaleTimeString('sr-Latn-RS', { hour: '2-digit', minute: '2-digit' })

/** The bell: unread count, linking to the list. */
export function NotificationBell() {
  const unread = useQuery({
    queryKey: ['notifications-unread'],
    queryFn: notificationsApi.unread,
    refetchInterval: 60_000,
  })
  const count = unread.data?.unread ?? 0
  return (
    <Link
      to="/notifications"
      className="inline-flex items-center gap-1.5 text-[13px] text-ink-2 hover:text-ink"
      aria-label={`Obaveštenja, nepročitanih: ${count}`}
    >
      Obaveštenja
      {count > 0 && (
        <span className="rounded-full bg-warn-soft px-[7px] text-[11.5px] font-semibold text-warn">
          {count}
        </span>
      )}
    </Link>
  )
}

export function NotificationsPage() {
  const queryClient = useQueryClient()
  const navigate = useNavigate()
  const [before, setBefore] = useState<string | undefined>()
  const [show, setShow] = useState<'all' | 'unread'>('all')
  const list = useQuery({
    queryKey: ['notifications', before],
    queryFn: () => notificationsApi.list(before),
  })
  const open = useMutation({
    mutationFn: async ({ id, link, read }: { id: string; link: string; read: boolean }) => {
      if (!read) {
        await notificationsApi.read(id)
      }
      return link
    },
    onSuccess: async (link) => {
      await queryClient.invalidateQueries({ queryKey: ['notifications-unread'] })
      await queryClient.invalidateQueries({ queryKey: ['notifications'] })
      navigate(link)
    },
  })
  const items = (list.data?.items ?? []).filter((n) => show === 'all' || !n.readAt)
  const unread = (list.data?.items ?? []).filter((n) => !n.readAt).length
  const days = items.reduce<{ day: string; items: typeof items }[]>((groups, n) => {
    const day = dayLabel(n.createdAt)
    const last = groups[groups.length - 1]
    if (last?.day === day) {
      last.items.push(n)
    } else {
      groups.push({ day, items: [n] })
    }
    return groups
  }, [])

  return (
    <>
      <PageHeader
        eyebrow={
          unread > 0 ? `${unread} nepročitan${unread === 1 ? 'o' : 'ih'}` : 'Sve je pročitano'
        }
        title="Obaveštenja"
        actions={
          <Link to="/notification-preferences" className={buttonClass('secondary')}>
            Podešavanja
          </Link>
        }
      />
      <Segmented
        label="Prikaži"
        value={show}
        onChange={setShow}
        options={[
          { value: 'all', label: 'Sva' },
          { value: 'unread', label: `Nepročitana${unread > 0 ? ` (${unread})` : ''}` },
        ]}
      />
      <ErrorMessage error={list.error ?? open.error} />
      {list.isSuccess && items.length === 0 && (
        <div className="flex flex-col items-center gap-2 rounded-lg border border-line bg-surface px-6 py-10 text-center">
          <KindIcon type="none" />
          <div className="font-semibold">
            {show === 'unread' ? 'Nema nepročitanih obaveštenja' : 'Još nema obaveštenja'}
          </div>
          <div className="max-w-sm text-[13px] text-ink-2">
            Ovde stižu nove ponude, koraci posla na kojima ste na potezu, odluke o proverama i
            bezbednosna upozorenja.
          </div>
        </div>
      )}
      {days.map((group) => (
        <section key={group.day} className="flex flex-col gap-2">
          <h2 className="text-[11.5px] font-semibold tracking-[0.08em] text-ink-3 uppercase">
            {group.day}
          </h2>
          <ul className="overflow-hidden rounded-lg border border-line bg-surface">
            {group.items.map((n) => {
              const kind = kindOf(n.type)
              return (
                <li key={n.id} className="border-b border-line last:border-b-0">
                  <button
                    type="button"
                    onClick={() => open.mutate({ id: n.id, link: n.link, read: Boolean(n.readAt) })}
                    className={`flex w-full items-center gap-3.5 px-4 py-3 text-left hover:bg-surface-2 ${
                      n.readAt ? '' : 'bg-accent-soft/40'
                    }`}
                  >
                    <KindIcon type={n.type} />
                    <span className="min-w-0 flex-1">
                      <span className={`block ${n.readAt ? 'text-ink-2' : 'font-semibold'}`}>
                        {kind.label}
                      </span>
                      <span className="block text-[12.5px] text-ink-3">
                        {kind.group}
                        {n.reference && (
                          <>
                            {' · '}
                            <span className="font-mono">{n.reference}</span>
                          </>
                        )}
                      </span>
                    </span>
                    <span className="flex flex-none items-center gap-2.5 text-[12.5px] text-ink-3 tabular-nums">
                      {time(n.createdAt)}
                      {!n.readAt && (
                        <span className="size-2 rounded-full bg-accent" aria-label="nepročitano" />
                      )}
                    </span>
                  </button>
                </li>
              )
            })}
          </ul>
        </section>
      ))}
      {list.data?.next && (
        <div>
          <Button variant="secondary" onClick={() => setBefore(list.data!.next!)}>
            Starija obaveštenja
          </Button>
        </div>
      )}
    </>
  )
}

export function NotificationPreferencesPage() {
  const queryClient = useQueryClient()
  const preferences = useQuery({
    queryKey: ['notification-preferences'],
    queryFn: notificationsApi.preferences,
  })
  const save = useMutation({
    mutationFn: (change: {
      language?: string
      type?: string
      channel?: string
      enabled?: boolean
    }) =>
      notificationsApi.setPreferences(
        change.language ?? preferences.data!.language,
        change.type
          ? [{ type: change.type, channel: change.channel!, enabled: change.enabled! }]
          : [],
      ),
    onSuccess: (updated) => queryClient.setQueryData(['notification-preferences'], updated),
  })
  const types = [...new Set(preferences.data?.items.map((i) => i.type))]
  const groups = [...new Set(types.map((t) => kindOf(t).group))]
  return (
    <>
      <div className="flex gap-1.5 text-[12.5px] text-ink-3">
        <Link to="/notifications" className="text-ink-2 underline underline-offset-2">
          Obaveštenja
        </Link>
        <span>/</span>
        <span>Podešavanja</span>
      </div>
      <PageHeader eyebrow="Obaveštenja" title="Podešavanja obaveštenja" />
      <ErrorMessage error={preferences.error ?? save.error} />
      {preferences.data && (
        <>
          <Panel title="Jezik mejlova">
            <Segmented
              label="Mejlovi stižu na"
              value={preferences.data.language}
              onChange={(language) => save.mutate({ language })}
              options={[
                { value: 'sr', label: 'Srpskom' },
                { value: 'en', label: 'Engleskom' },
              ]}
            />
          </Panel>
          {groups.map((group) => (
            <Panel
              key={group}
              title={group}
              padded={false}
              actions={
                <span className="flex gap-6 pr-1 text-[12px] font-semibold text-ink-3">
                  <span className="w-12 text-center">U aplikaciji</span>
                  <span className="w-12 text-center">Email</span>
                </span>
              }
            >
              <ul>
                {types
                  .filter((type) => kindOf(type).group === group)
                  .map((type) => (
                    <li
                      key={type}
                      className="flex items-center gap-3.5 border-b border-line px-4 py-3 last:border-b-0"
                    >
                      <KindIcon type={type} />
                      <span className="min-w-0 flex-1">
                        {kindOf(type).label}
                        {preferences.data.items.some((i) => i.type === type && i.mandatory) && (
                          <span className="ml-2 rounded-full bg-surface-2 px-2 py-0.5 text-[11px] font-semibold text-ink-3">
                            obavezno
                          </span>
                        )}
                      </span>
                      <span className="flex gap-6">
                        {(['inapp', 'email'] as const).map((channel) => {
                          const row = preferences.data.items.find(
                            (i) => i.type === type && i.channel === channel,
                          )!
                          return (
                            <span key={channel} className="flex w-12 justify-center">
                              <Switch
                                label={`${kindOf(type).label}: ${channel === 'inapp' ? 'u aplikaciji' : 'email'}`}
                                checked={row.enabled}
                                disabled={row.mandatory || save.isPending}
                                onChange={(enabled) => save.mutate({ type, channel, enabled })}
                              />
                            </span>
                          )
                        })}
                      </span>
                    </li>
                  ))}
              </ul>
            </Panel>
          ))}
          <p className="rounded-md bg-surface-2 px-3 py-2.5 text-[12.5px] text-ink-2">
            Bezbednosna obaveštenja i koraci posla na kojima ste na potezu ne mogu da se isključe.
          </p>
        </>
      )}
    </>
  )
}

function rsdOrNull(value: string) {
  return value.trim() === '' ? null : { amount: value.trim(), currency: 'RSD' }
}

function numberOrNull(value: string) {
  return value.trim() === '' ? null : Number(value)
}

/** The factor's appetite filters for the hourly new-listing digest (task 019). */
export function FactorAppetiteSection({ organizationId }: { organizationId: string }) {
  const appetite = useQuery({
    queryKey: ['factor-appetite', organizationId],
    queryFn: () => notificationsApi.appetite(organizationId),
  })
  return appetite.data ? (
    <AppetiteForm
      key={JSON.stringify(appetite.data)}
      organizationId={organizationId}
      initial={appetite.data}
    />
  ) : null
}

function AppetiteForm({
  organizationId,
  initial,
}: {
  organizationId: string
  initial: FactorAppetite
}) {
  const queryClient = useQueryClient()
  const [minAmount, setMinAmount] = useState(initial.minAmount?.amount ?? '')
  const [maxAmount, setMaxAmount] = useState(initial.maxAmount?.amount ?? '')
  const [minTenor, setMinTenor] = useState(initial.minTenorDays?.toString() ?? '')
  const [maxTenor, setMaxTenor] = useState(initial.maxTenorDays?.toString() ?? '')
  const [digest, setDigest] = useState(initial.digestEnabled)
  const save = useMutation({
    mutationFn: () =>
      notificationsApi.setAppetite(organizationId, {
        minAmount: rsdOrNull(minAmount),
        maxAmount: rsdOrNull(maxAmount),
        minTenorDays: numberOrNull(minTenor),
        maxTenorDays: numberOrNull(maxTenor),
        digestEnabled: digest,
      }),
    onSuccess: (updated) => queryClient.setQueryData(['factor-appetite', organizationId], updated),
  })
  return (
    <form className="space-y-2 text-sm" onSubmit={(e) => (e.preventDefault(), save.mutate())}>
      <h2 className="font-medium">Filteri za nove oglase (RSD)</h2>
      <div className="grid grid-cols-2 gap-2">
        <Field
          id="minAmount"
          label="Iznos od"
          inputMode="decimal"
          value={minAmount}
          onChange={(e) => setMinAmount(e.target.value)}
        />
        <Field
          id="maxAmount"
          label="Iznos do"
          inputMode="decimal"
          value={maxAmount}
          onChange={(e) => setMaxAmount(e.target.value)}
        />
        <Field
          id="minTenor"
          label="Dana do dospeća od"
          inputMode="numeric"
          value={minTenor}
          onChange={(e) => setMinTenor(e.target.value)}
        />
        <Field
          id="maxTenor"
          label="Dana do dospeća do"
          inputMode="numeric"
          value={maxTenor}
          onChange={(e) => setMaxTenor(e.target.value)}
        />
      </div>
      <label className="block">
        <input type="checkbox" checked={digest} onChange={(e) => setDigest(e.target.checked)} />{' '}
        Šalji mi pregled novih oglasa (najviše jednom na sat)
      </label>
      <ErrorMessage error={save.error} />
      <Button type="submit" variant="secondary" disabled={save.isPending}>
        Sačuvaj filtere
      </Button>
    </form>
  )
}
