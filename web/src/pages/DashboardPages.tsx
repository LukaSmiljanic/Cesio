import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { Link } from 'react-router'
import { api, download } from '../api/client'
import { organizationsApi } from '../api/organizations'
import { formatMoney } from '../api/receivables'
import { useStepUp } from '../auth/stepUpContext'
import {
  Button,
  Card,
  Chip,
  EmptyRow,
  ErrorMessage,
  Field,
  Kpis,
  PageHeader,
  Panel,
} from '../components/ui'
import { buttonClass } from '../components/styles'
import { useActiveOrganization } from '../organizations/activeOrganization'

interface Money {
  amount: string
  currency: string
}

const money = (list: Money[] | undefined) =>
  list && list.length > 0 ? list.map((m) => formatMoney(m)).join(' · ') : '0,00 RSD'

const percent = (rate: number | null | undefined) =>
  rate === null || rate === undefined ? '—' : `${(rate * 100).toFixed(2).replace('.', ',')} %`

interface SellerBoard {
  receivablesByState: Record<string, number>
  openListings: { listingId: string; offerCount: number; bestNetProceeds: Money | null }[]
  dealsNeedingAction: number
  fundedThisMonth: Money[]
  fundedThisYear: Money[]
  averageDiscountRate: { currency: string; rate: number }[]
  feesOutstanding: Money[]
}

interface FactorBoard {
  activeOffers: number
  winRate: number | null
  dealsNeedingAction: number
  portfolio: { bucket: string; count: number; faceValue: Money[] }[]
  overdueOutcomes: number
  fundedThisMonth: Money[]
  feesOutstanding: Money[]
}

interface ActionItem {
  title: string
  detail: string
  to: string
  button: string
}

function ActionList({ items }: { items: ActionItem[] }) {
  return (
    <Panel
      title="Čeka vašu akciju"
      actions={
        <span className="text-[12.5px] text-ink-3">
          {items.length} {items.length === 1 ? 'stavka' : 'stavke'}
        </span>
      }
      padded={false}
    >
      {items.length === 0 ? (
        <div className="px-[18px] py-5 text-ink-2">Trenutno ništa ne čeka vašu akciju.</div>
      ) : (
        <ul>
          {items.map((item) => (
            <li
              key={item.to + item.title}
              className="flex flex-wrap items-center justify-between gap-3.5 border-b border-line px-[18px] py-3 last:border-b-0"
            >
              <div>
                <div className="font-medium">{item.title}</div>
                <div className="text-[12.5px] text-ink-2">{item.detail}</div>
              </div>
              <Link to={item.to} className={buttonClass('primary', 'sm')}>
                {item.button}
              </Link>
            </li>
          ))}
        </ul>
      )}
    </Panel>
  )
}

/** The seller's or the factor's dashboard, by the active organization (task 022). */
export function DashboardPage({ type }: { type: 'Seller' | 'Factor' }) {
  const { activeId } = useActiveOrganization()
  const mine = useQuery({ queryKey: ['organizations'], queryFn: organizationsApi.mine })
  const organization = mine.data?.find((o) => o.id === activeId)
  const board = useQuery({
    queryKey: ['dashboard', type, activeId],
    queryFn: () => api<SellerBoard | FactorBoard>('GET', `/dashboards/${type.toLowerCase()}`),
  })
  const exportDeals = useMutation({ mutationFn: () => download('/deals/export', 'deals.csv') })
  const exportReceivables = useMutation({
    mutationFn: () => download('/receivables/export', 'receivables.csv'),
  })
  const name = organization ? (organization.legalName ?? `PIB ${organization.pib}`) : ''
  const eyebrow = `${type === 'Seller' ? 'Prodavac' : 'Faktor'} · ${name}`
  if (!board.data) {
    return (
      <>
        <PageHeader eyebrow={eyebrow} title="Pregled" />
        <ErrorMessage error={board.error} />
      </>
    )
  }
  if (type === 'Seller') {
    const b = board.data as SellerBoard
    const offers = b.openListings.reduce((n, l) => n + l.offerCount, 0)
    const verified = b.receivablesByState.Verified ?? 0
    const drafts = b.receivablesByState.Draft ?? 0
    const actions: ActionItem[] = [
      ...b.openListings
        .filter((l) => l.offerCount > 0)
        .map((l) => ({
          title: `Oglas ima ${l.offerCount} ${l.offerCount === 1 ? 'ponudu' : 'ponude'}`,
          detail: l.bestNetProceeds
            ? `Najbolja: dobijate ${formatMoney(l.bestNetProceeds)}`
            : 'Uporedite ponude',
          to: `/listings/${l.listingId}/offers`,
          button: 'Uporedi ponude',
        })),
      ...(b.dealsNeedingAction > 0
        ? [
            {
              title: `Poslovi u toku: ${b.dealsNeedingAction}`,
              detail: 'Potpis, isplata ili potvrda prijema',
              to: '/deals',
              button: 'Otvori poslove',
            },
          ]
        : []),
      ...(verified > 0
        ? [
            {
              title: `Verifikovano, nije objavljeno: ${verified}`,
              detail: 'Spremno za tržište',
              to: '/receivables',
              button: 'Objavi',
            },
          ]
        : []),
      ...(drafts > 0
        ? [
            {
              title: `Nacrti: ${drafts}`,
              detail: 'Dopunite izjavu i dokumente, pa pošaljite na proveru',
              to: '/receivables',
              button: 'Dopuni',
            },
          ]
        : []),
    ]
    return (
      <>
        <PageHeader
          eyebrow={eyebrow}
          title="Pregled"
          actions={
            <Link to="/receivables/import" className={buttonClass()}>
              Uvezi fakture iz SEF-a
            </Link>
          }
        />
        <ActionList items={actions} />
        <Kpis
          items={[
            {
              label: 'Na tržištu',
              value: b.openListings.length,
              sub: `${offers} ${offers === 1 ? 'ponuda' : 'ponuda'} na oglasima`,
            },
            { label: 'Poslovi u toku', value: b.dealsNeedingAction, sub: 'pre isplate' },
            {
              label: 'Isplaćeno ovog meseca',
              value: money(b.fundedThisMonth),
              sub: `ove godine ${money(b.fundedThisYear)}`,
            },
            {
              label: 'Prosečan diskont (okvirno)',
              value: b.averageDiscountRate.map((r) => percent(r.rate)).join(' · ') || '—',
              sub:
                b.averageDiscountRate.map((r) => r.currency).join(' · ') ||
                'još nema isplaćenih poslova',
            },
          ]}
        />
        <div className="flex flex-wrap gap-2">
          <Button variant="secondary" size="sm" onClick={() => exportReceivables.mutate()}>
            Izvoz potraživanja (CSV)
          </Button>
          <Button variant="secondary" size="sm" onClick={() => exportDeals.mutate()}>
            Izvoz poslova (CSV)
          </Button>
        </div>
      </>
    )
  }
  const b = board.data as FactorBoard
  const actions: ActionItem[] = [
    ...(b.dealsNeedingAction > 0
      ? [
          {
            title: `Poslovi u toku: ${b.dealsNeedingAction}`,
            detail: 'Provera, potpis ili prijava isplate',
            to: '/deals',
            button: 'Otvori poslove',
          },
        ]
      : []),
    ...(b.overdueOutcomes > 0
      ? [
          {
            title: `Zakasneli ishodi: ${b.overdueOutcomes}`,
            detail: 'Upišite ishod naplate od dužnika',
            to: '/deals',
            button: 'Upiši ishod',
          },
        ]
      : []),
  ]
  return (
    <>
      <PageHeader
        eyebrow={eyebrow}
        title="Pregled"
        actions={
          <Link to="/marketplace" className={buttonClass()}>
            Otvori tržište
          </Link>
        }
      />
      <ActionList items={actions} />
      <Kpis
        items={[
          {
            label: 'Vaše aktivne ponude',
            value: b.activeOffers,
            sub: `uspešnost ${percent(b.winRate)}`,
          },
          { label: 'Isplaćeno ovog meseca', value: money(b.fundedThisMonth) },
          {
            label: 'Neplaćene naknade',
            value: money(b.feesOutstanding),
            sub: '15 % diskonta, plaća faktor',
          },
          { label: 'Zakasneli ishodi', value: b.overdueOutcomes },
        ]}
      />
      <Panel title="Portfolio po dospeću" padded={false}>
        <table>
          <thead>
            <tr>
              <th>Dospeće</th>
              <th className="text-right">Broj</th>
              <th className="text-right">Nominalni iznos</th>
            </tr>
          </thead>
          <tbody>
            {b.portfolio.map((p) => (
              <tr key={p.bucket}>
                <td>{p.bucket === 'overdue' ? 'Dospelo' : `${p.bucket} dana`}</td>
                <td className="text-right">{p.count}</td>
                <td className="text-right font-medium">{money(p.faceValue)}</td>
              </tr>
            ))}
            {b.portfolio.length === 0 && (
              <EmptyRow columns={3}>Još nema finansiranih poslova.</EmptyRow>
            )}
          </tbody>
        </table>
      </Panel>
      <div>
        <Button variant="secondary" size="sm" onClick={() => exportDeals.mutate()}>
          Izvoz poslova (CSV)
        </Button>
      </div>
    </>
  )
}

interface AdminBoard {
  queues: {
    kyb: number
    factorAuthorizations: number
    receivableReview: number
    bankAccounts: number
    pristupnice: number
    mfaResets: number
    activeHolds: number
  }
  stuckDeals: { state: string; count: number }[]
  month: { funded: number; gmv: Money[]; platformFees: Money[] }
  funnel: {
    imported: number
    verifiedNow: number
    listed: number
    withOffers: number
    accepted: number
    funded: number
  }
  integrationHealth: {
    invalidSefConnections: number
    outbox: { pending: number; deadLettered: number }
    failedEmails24h: number
    jobs: { name: string; lastStatus: string | null; failuresLast24h: number }[]
  }
}

/** Staff: queues, stuck deals, the month and integration health (task 022). */
export function AdminDashboardPage() {
  const board = useQuery({
    queryKey: ['admin-dashboard'],
    queryFn: () => api<AdminBoard>('GET', '/admin/dashboard'),
  })
  const b = board.data
  const queues: [string, number, string][] = b
    ? [
        ['KYB provere', b.queues.kyb, '/admin/kyb'],
        ['Ovlašćenja faktora', b.queues.factorAuthorizations, '/admin/factor-authorizations'],
        ['Potraživanja na proveri', b.queues.receivableReview, '/admin/review'],
        ['Računi za isplatu', b.queues.bankAccounts, '/admin/bank-accounts'],
        ['Pristupnice', b.queues.pristupnice, '/admin/pristupnice'],
        ['MFA resetovanja', b.queues.mfaResets, '/admin/mfa-resets'],
        ['Aktivne blokade', b.queues.activeHolds, '/admin/kyb'],
      ]
    : []
  const failingJobs = b?.integrationHealth.jobs.filter((j) => j.failuresLast24h > 0) ?? []
  return (
    <>
      <PageHeader eyebrow="Platforma Cesio" title="Pregled" />
      <ErrorMessage error={board.error} />
      {b && (
        <>
          <Kpis
            items={[
              { label: 'Finansirano ovog meseca', value: b.month.funded, sub: 'poslova' },
              {
                label: 'GMV ovog meseca',
                value: money(b.month.gmv),
                sub: 'zbir nominalnih iznosa',
              },
              {
                label: 'Naknade platforme',
                value: money(b.month.platformFees),
                sub: '15 % diskonta',
              },
              {
                label: 'Zaglavljeni poslovi',
                value: b.stuckDeals.reduce((n, s) => n + s.count, 0),
                sub: 'bez promene duže od 5 dana',
              },
            ]}
          />
          <div className="grid items-start gap-6 lg:grid-cols-[minmax(0,1.6fr)_minmax(0,1fr)]">
            <Panel title="Redovi čekanja" padded={false}>
              <ul>
                {queues.map(([label, count, to]) => (
                  <li
                    key={label}
                    className="flex items-center justify-between gap-3 border-b border-line px-[18px] py-3 last:border-b-0"
                  >
                    <Link to={to} className="font-medium hover:underline">
                      {label}
                    </Link>
                    {count > 0 ? <Chip tone="warn">{count}</Chip> : <Chip>0</Chip>}
                  </li>
                ))}
              </ul>
            </Panel>
            <div className="flex flex-col gap-4">
              <Panel title="Tok ovog meseca">
                <dl className="grid grid-cols-[1fr_auto] gap-x-4 gap-y-2 text-[13.5px]">
                  {(
                    [
                      ['Uvezene fakture', b.funnel.imported],
                      ['Verifikovano (sada)', b.funnel.verifiedNow],
                      ['Objavljeno na tržištu', b.funnel.listed],
                      ['Oglasi sa ponudom', b.funnel.withOffers],
                      ['Prihvaćene ponude', b.funnel.accepted],
                      ['Finansirano', b.funnel.funded],
                    ] as [string, number][]
                  ).map(([label, value]) => (
                    <div key={label} className="contents">
                      <dt className="text-ink-2">{label}</dt>
                      <dd className="text-right font-medium tabular-nums">{value}</dd>
                    </div>
                  ))}
                </dl>
              </Panel>
              <Panel title="Integracije">
                <dl className="grid grid-cols-[1fr_auto] gap-x-4 gap-y-2 text-[13.5px]">
                  <dt className="text-ink-2">Nevažeće SEF veze</dt>
                  <dd className="text-right">{b.integrationHealth.invalidSefConnections}</dd>
                  <dt className="text-ink-2">Outbox na čekanju / neisporučeno</dt>
                  <dd className="text-right">
                    {b.integrationHealth.outbox.pending} / {b.integrationHealth.outbox.deadLettered}
                  </dd>
                  <dt className="text-ink-2">Neuspeli mejlovi (24 h)</dt>
                  <dd className="text-right">{b.integrationHealth.failedEmails24h}</dd>
                  <dt className="text-ink-2">Pozadinski poslovi sa greškom (24 h)</dt>
                  <dd className="text-right">
                    {failingJobs.length > 0 ? (
                      <Chip tone="crit">{failingJobs.length}</Chip>
                    ) : (
                      <Chip tone="pos">0</Chip>
                    )}
                  </dd>
                </dl>
              </Panel>
            </div>
          </div>
        </>
      )}
    </>
  )
}

interface ParameterVersion {
  key: string
  version: number
  value: string
  effectiveFrom: string
}

/** PlatformAdmin and Finance: versioned business parameters; a money-related change waits for a second person. */
export function AdminParametersPage() {
  const queryClient = useQueryClient()
  const withStepUp = useStepUp()
  const [key, setKey] = useState('')
  const [value, setValue] = useState('')
  const data = useQuery({
    queryKey: ['admin-parameters'],
    queryFn: () =>
      api<{ current: ParameterVersion[]; pending: { id: string; key: string; value: string }[] }>(
        'GET',
        '/admin/parameters',
      ),
  })
  const refresh = () => queryClient.invalidateQueries({ queryKey: ['admin-parameters'] })
  const change = useMutation({
    mutationFn: () =>
      withStepUp(() =>
        api('PUT', `/admin/parameters/${encodeURIComponent(key)}`, { value: JSON.parse(value) }),
      ),
    onSuccess: refresh,
  })
  const approve = useMutation({
    mutationFn: (id: string) =>
      withStepUp(() => api('POST', `/admin/parameters/proposals/${id}/approve`)),
    onSuccess: refresh,
  })
  return (
    <Card title="Poslovni parametri">
      <ErrorMessage error={data.error ?? change.error ?? approve.error} />
      <ul className="space-y-1 text-sm">
        {data.data?.current.map((p) => (
          <li key={p.key}>
            {p.key} = {p.value} (v{p.version})
          </li>
        ))}
      </ul>
      {data.data?.pending.length ? (
        <ul className="mt-4 space-y-1 text-sm">
          {data.data.pending.map((p) => (
            <li key={p.id} className="flex items-center justify-between">
              <span>
                Predlog: {p.key} = {p.value}
              </span>
              <Button variant="secondary" onClick={() => approve.mutate(p.id)}>
                Odobri
              </Button>
            </li>
          ))}
        </ul>
      ) : null}
      <form
        className="mt-4 space-y-2 text-sm"
        onSubmit={(e) => (e.preventDefault(), change.mutate())}
      >
        <Field
          id="paramKey"
          label="Ključ (npr. marketplace.maxListingDays)"
          value={key}
          onChange={(e) => setKey(e.target.value)}
        />
        <Field
          id="paramValue"
          label="Vrednost (JSON)"
          value={value}
          onChange={(e) => setValue(e.target.value)}
        />
        <Button type="submit" disabled={!key || !value || change.isPending}>
          Sačuvaj novu verziju
        </Button>
      </form>
    </Card>
  )
}

interface AuditRow {
  sequence: number
  occurredAt: string
  actorType: string
  action: string
  entityType: string
  entityId: string
  reason: string | null
}

/** Compliance and Auditors: search the audit log and export it (both access-logged). */
export function AdminAuditPage() {
  const withStepUp = useStepUp()
  const [entityId, setEntityId] = useState('')
  const [action, setAction] = useState('')
  const query = new URLSearchParams()
  if (entityId) query.set('entityId', entityId)
  if (action) query.set('action', action)
  const rows = useQuery({
    queryKey: ['admin-audit', entityId, action],
    queryFn: () => api<{ items: AuditRow[] }>('GET', `/admin/audit?${query.toString()}`),
  })
  const exportCsv = useMutation({
    mutationFn: () =>
      withStepUp(() => download(`/admin/audit/export?${query.toString()}`, 'audit.csv')),
  })
  return (
    <Card title="Audit">
      <div className="space-y-2 text-sm">
        <Field
          id="auditEntity"
          label="ID entiteta"
          value={entityId}
          onChange={(e) => setEntityId(e.target.value)}
        />
        <Field
          id="auditAction"
          label="Akcija (početak, npr. Deal.)"
          value={action}
          onChange={(e) => setAction(e.target.value)}
        />
        <Button variant="secondary" onClick={() => exportCsv.mutate()}>
          Izvoz (CSV)
        </Button>
      </div>
      <ErrorMessage error={rows.error ?? exportCsv.error} />
      <ul className="mt-4 space-y-1 text-xs">
        {rows.data?.items.map((r) => (
          <li key={r.sequence}>
            {new Date(r.occurredAt).toLocaleString('sr-RS')} · {r.actorType} · {r.action} ·{' '}
            {r.entityType} {r.entityId.slice(0, 8)}
            {r.reason && ` · ${r.reason}`}
          </li>
        ))}
      </ul>
    </Card>
  )
}
