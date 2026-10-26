import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { api, download } from '../api/client'
import { useStepUp } from '../auth/stepUpContext'
import { Button, Card, ErrorMessage, Field } from '../components/ui'
import { useActiveOrganization } from '../organizations/activeOrganization'

interface Money {
  amount: string
  currency: string
}

const money = (list: Money[] | undefined) =>
  list && list.length > 0 ? list.map((m) => `${m.amount} ${m.currency}`).join(', ') : '0'

const percent = (rate: number | null | undefined) =>
  rate === null || rate === undefined ? '—' : `${(rate * 100).toFixed(2)} %`

function Figures({ rows }: { rows: [string, React.ReactNode][] }) {
  return (
    <dl className="grid grid-cols-2 gap-1 text-sm">
      {rows.map(([label, value]) => (
        <div key={label} className="contents">
          <dt>{label}</dt>
          <dd>{value}</dd>
        </div>
      ))}
    </dl>
  )
}

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

/** The seller's or the factor's dashboard, by the active organization (task 022). */
export function DashboardPage({ type }: { type: 'Seller' | 'Factor' }) {
  const { activeId } = useActiveOrganization()
  const board = useQuery({
    queryKey: ['dashboard', type, activeId],
    queryFn: () => api<SellerBoard | FactorBoard>('GET', `/dashboards/${type.toLowerCase()}`),
  })
  const exportDeals = useMutation({ mutationFn: () => download('/deals/export', 'deals.csv') })
  const exportReceivables = useMutation({
    mutationFn: () => download('/receivables/export', 'receivables.csv'),
  })
  if (!board.data) {
    return (
      <Card title="Pregled">
        <ErrorMessage error={board.error} />
      </Card>
    )
  }
  if (type === 'Seller') {
    const b = board.data as SellerBoard
    return (
      <Card title="Pregled prodavca">
        <Figures
          rows={[
            [
              'Potraživanja',
              Object.entries(b.receivablesByState)
                .map(([s, n]) => `${s}: ${n}`)
                .join(', ') || '—',
            ],
            ['Otvoreni oglasi', b.openListings.length],
            ['Ponude na oglasima', b.openListings.reduce((n, l) => n + l.offerCount, 0)],
            ['Poslovi koji čekaju', b.dealsNeedingAction],
            ['Finansirano ovog meseca', money(b.fundedThisMonth)],
            ['Finansirano ove godine', money(b.fundedThisYear)],
            [
              'Prosečan diskont (okvirno)',
              b.averageDiscountRate.map((r) => `${percent(r.rate)} (${r.currency})`).join(', ') ||
                '—',
            ],
            ['Neplaćene naknade', money(b.feesOutstanding)],
          ]}
        />
        <div className="mt-4 flex gap-2">
          <Button variant="secondary" onClick={() => exportReceivables.mutate()}>
            Izvoz potraživanja (CSV)
          </Button>
          <Button variant="secondary" onClick={() => exportDeals.mutate()}>
            Izvoz poslova (CSV)
          </Button>
        </div>
      </Card>
    )
  }
  const b = board.data as FactorBoard
  return (
    <Card title="Pregled faktora">
      <Figures
        rows={[
          ['Aktivne ponude', b.activeOffers],
          ['Uspešnost ponuda', percent(b.winRate)],
          ['Poslovi koji čekaju', b.dealsNeedingAction],
          [
            'Portfolio po dospeću',
            b.portfolio.map((p) => `${p.bucket}: ${p.count} (${money(p.faceValue)})`).join(', ') ||
              '—',
          ],
          ['Zakasneli ishodi', b.overdueOutcomes],
          ['Finansirano ovog meseca', money(b.fundedThisMonth)],
          ['Neplaćene naknade', money(b.feesOutstanding)],
        ]}
      />
      <div className="mt-4">
        <Button variant="secondary" onClick={() => exportDeals.mutate()}>
          Izvoz poslova (CSV)
        </Button>
      </div>
    </Card>
  )
}

/** Staff: queues, stuck deals, the month and integration health (task 022). */
export function AdminDashboardPage() {
  const board = useQuery({
    queryKey: ['admin-dashboard'],
    queryFn: () => api<Record<string, unknown>>('GET', '/admin/dashboard'),
  })
  return (
    <Card title="Administracija">
      <ErrorMessage error={board.error} />
      {board.data && (
        <pre className="overflow-x-auto text-xs">{JSON.stringify(board.data, null, 2)}</pre>
      )}
    </Card>
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
