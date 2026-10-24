import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { Link, useNavigate } from 'react-router'
import { notificationsApi, type FactorAppetite } from '../api/notifications'
import { Button, Card, ErrorMessage, Field } from '../components/ui'

const notificationLabels: Record<string, string> = {
  deal_action_required: 'Potrebna je vaša radnja na poslu',
  funding_reported: 'Faktor je prijavio isplatu',
  funding_confirmed: 'Isplata je potvrđena',
  deal_cancelled: 'Posao je otkazan',
  offer_received: 'Nova ponuda',
  offer_withdrawn: 'Ponuda je povučena',
  offer_accepted: 'Vaša ponuda je prihvaćena',
  offer_superseded: 'Vaša ponuda više nije aktivna',
  new_listing: 'Novi oglas po vašim filterima',
  receivable_verification: 'Provera potraživanja je završena',
  kyb_decision: 'Odluka o proveri firme',
  factor_authorization: 'Promena ovlašćenja za faktoring',
  hold: 'Promena blokade',
  sef_status_changed: 'SEF status fakture se promenio',
  fee_statement: 'Izdat je obračun naknade',
  security: 'Bezbednosno obaveštenje',
  reminder: 'Podsetnik',
}

/** The bell: unread count, linking to the list. */
export function NotificationBell() {
  const unread = useQuery({
    queryKey: ['notifications-unread'],
    queryFn: notificationsApi.unread,
    refetchInterval: 60_000,
  })
  return (
    <Link className="text-sm underline" to="/notifications" aria-label="Obaveštenja">
      🔔 {unread.data?.unread ?? 0}
    </Link>
  )
}

export function NotificationsPage() {
  const queryClient = useQueryClient()
  const navigate = useNavigate()
  const [before, setBefore] = useState<string | undefined>()
  const list = useQuery({
    queryKey: ['notifications', before],
    queryFn: () => notificationsApi.list(before),
  })
  const open = useMutation({
    mutationFn: async ({ id, link }: { id: string; link: string }) => {
      await notificationsApi.read(id)
      return link
    },
    onSuccess: async (link) => {
      await queryClient.invalidateQueries({ queryKey: ['notifications-unread'] })
      navigate(link)
    },
  })
  return (
    <Card title="Obaveštenja">
      <ErrorMessage error={list.error ?? open.error} />
      {list.data?.items.length === 0 && <p className="text-sm">Nema obaveštenja.</p>}
      <ul className="space-y-2 text-sm">
        {list.data?.items.map((n) => (
          <li key={n.id}>
            <button
              type="button"
              className={`text-left underline ${n.readAt ? 'text-slate-500' : 'font-medium'}`}
              onClick={() => open.mutate({ id: n.id, link: n.link })}
            >
              {notificationLabels[n.type] ?? n.type}
              {n.reference && ` ${n.reference}`}
            </button>{' '}
            <span className="text-slate-500">{new Date(n.createdAt).toLocaleString('sr-RS')}</span>
          </li>
        ))}
      </ul>
      <div className="mt-4 flex gap-4 text-sm">
        {list.data?.next && (
          <Button variant="secondary" onClick={() => setBefore(list.data!.next!)}>
            Starija
          </Button>
        )}
        <Link className="underline" to="/notification-preferences">
          Podešavanja
        </Link>
      </div>
    </Card>
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
  return (
    <Card title="Podešavanja obaveštenja">
      <ErrorMessage error={preferences.error ?? save.error} />
      {preferences.data && (
        <div className="space-y-3 text-sm">
          <label className="block">
            <span className="block font-medium">Jezik mejlova</span>
            <select
              className="h-10 w-full rounded-md border border-slate-300 bg-white px-3"
              value={preferences.data.language}
              onChange={(e) => save.mutate({ language: e.target.value })}
            >
              <option value="sr">Srpski</option>
              <option value="en">English</option>
            </select>
          </label>
          <table className="w-full">
            <thead>
              <tr>
                <th className="text-left">Vrsta</th>
                <th>U aplikaciji</th>
                <th>Email</th>
              </tr>
            </thead>
            <tbody>
              {types.map((type) => (
                <tr key={type}>
                  <td>{notificationLabels[type] ?? type}</td>
                  {(['inapp', 'email'] as const).map((channel) => {
                    const row = preferences.data.items.find(
                      (i) => i.type === type && i.channel === channel,
                    )!
                    return (
                      <td key={channel} className="text-center">
                        <input
                          type="checkbox"
                          aria-label={`${type} ${channel}`}
                          checked={row.enabled}
                          disabled={row.mandatory || save.isPending}
                          onChange={(e) =>
                            save.mutate({ type, channel, enabled: e.target.checked })
                          }
                        />
                      </td>
                    )
                  })}
                </tr>
              ))}
            </tbody>
          </table>
          <p className="text-slate-600">
            Bezbednosna obaveštenja i koraci posla ne mogu da se isključe.
          </p>
        </div>
      )}
    </Card>
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
