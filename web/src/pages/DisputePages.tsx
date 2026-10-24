import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { disputeKinds, disputesApi, type DisputeView } from '../api/disputes'
import { useStepUp } from '../auth/stepUpContext'
import { Button, Card, ErrorMessage, Field } from '../components/ui'

const kindLabels: Record<string, string> = {
  FundingDiscrepancy: 'Razlika u isplati',
  ContractMismatch: 'Ugovor se ne poklapa',
  InvoiceDisputedByDebtor: 'Dužnik osporava fakturu',
  FraudSuspicion: 'Sumnja na prevaru',
  FeeDispute: 'Spor o naknadi',
  Other: 'Drugo',
}

const statusLabels: Record<string, string> = {
  Open: 'otvoren',
  Resolved: 'rešen',
  Withdrawn: 'povučen',
}

function Thread({ view }: { view: DisputeView }) {
  return (
    <ul className="space-y-1">
      {view.comments.map((c) => (
        <li key={c.id} className={c.internal ? 'text-amber-700' : ''}>
          {c.byStaff ? 'Cesio' : 'Strana'}
          {c.internal && ' (interno)'}: {c.text ?? 'prilog'}{' '}
          <span className="text-slate-500">{new Date(c.createdAt).toLocaleString('sr-RS')}</span>
        </li>
      ))}
    </ul>
  )
}

function PartyDispute({ id, organizationId }: { id: string; organizationId: string | null }) {
  const queryClient = useQueryClient()
  const [text, setText] = useState('')
  const view = useQuery({ queryKey: ['dispute', id], queryFn: () => disputesApi.get(id) })
  const refresh = () => queryClient.invalidateQueries({ queryKey: ['dispute', id] })
  const comment = useMutation({
    mutationFn: () => disputesApi.comment(id, text),
    onSuccess: () => (setText(''), refresh()),
  })
  const withdraw = useMutation({
    mutationFn: () => disputesApi.withdraw(id),
    onSuccess: () => queryClient.invalidateQueries(),
  })
  const d = view.data?.dispute
  if (!d) {
    return <ErrorMessage error={view.error} />
  }
  return (
    <li className="space-y-2 border-t border-slate-200 pt-2">
      <p>
        {kindLabels[d.kind] ?? d.kind} · {statusLabels[d.status] ?? d.status} · {d.description}
      </p>
      {d.resolutionSummary && <p>Rešenje: {d.resolutionSummary}</p>}
      <Thread view={view.data!} />
      {d.status === 'Open' && (
        <form className="space-y-2" onSubmit={(e) => (e.preventDefault(), comment.mutate())}>
          <Field
            id={`comment-${id}`}
            label="Komentar"
            value={text}
            onChange={(e) => setText(e.target.value)}
          />
          <ErrorMessage error={comment.error ?? withdraw.error} />
          <div className="flex gap-2">
            <Button type="submit" variant="secondary" disabled={!text.trim() || comment.isPending}>
              Pošalji
            </Button>
            {d.openedByOrgId === organizationId && (
              <Button
                variant="secondary"
                onClick={() => withdraw.mutate()}
                disabled={withdraw.isPending}
              >
                Povuci spor
              </Button>
            )}
          </div>
        </form>
      )}
    </li>
  )
}

/** The deal page's disputes: open one (a fee dispute does not block the deal) and follow the thread (task 021). */
export function DealDisputes({
  dealId,
  organizationId,
}: {
  dealId: string
  organizationId: string | null
}) {
  const queryClient = useQueryClient()
  const [kind, setKind] = useState<string>('ContractMismatch')
  const [description, setDescription] = useState('')
  const list = useQuery({
    queryKey: ['deal-disputes', dealId],
    queryFn: () => disputesApi.forDeal(dealId),
  })
  const open = useMutation({
    mutationFn: () => disputesApi.open(dealId, kind, description),
    onSuccess: () => {
      setDescription('')
      return queryClient.invalidateQueries()
    },
  })
  return (
    <section className="space-y-2 text-sm">
      <h2 className="font-medium">Sporovi</h2>
      <ul className="space-y-2">
        {list.data?.map((d) => (
          <PartyDispute key={d.id} id={d.id} organizationId={organizationId} />
        ))}
      </ul>
      <form className="space-y-2" onSubmit={(e) => (e.preventDefault(), open.mutate())}>
        <label className="block">
          <span className="block font-medium">Vrsta spora</span>
          <select
            className="h-10 w-full rounded-md border border-slate-300 bg-white px-3"
            value={kind}
            onChange={(e) => setKind(e.target.value)}
          >
            {disputeKinds.map((k) => (
              <option key={k} value={k}>
                {kindLabels[k]}
              </option>
            ))}
          </select>
        </label>
        <Field
          id="disputeDescription"
          label="Opis"
          required
          value={description}
          onChange={(e) => setDescription(e.target.value)}
        />
        <p className="text-slate-600">
          Spor (osim spora o naknadi) zaustavlja dalje korake posla dok ga Cesio ne reši.
        </p>
        <ErrorMessage error={open.error ?? list.error} />
        <Button type="submit" variant="secondary" disabled={open.isPending}>
          Otvori spor
        </Button>
      </form>
    </section>
  )
}

function StaffDispute({ id }: { id: string }) {
  const queryClient = useQueryClient()
  const withStepUp = useStepUp()
  const [text, setText] = useState('')
  const [internal, setInternal] = useState(true)
  const [summary, setSummary] = useState('')
  const view = useQuery({
    queryKey: ['staff-dispute', id],
    queryFn: () => disputesApi.staffGet(id),
  })
  const refresh = () => queryClient.invalidateQueries()
  const comment = useMutation({
    mutationFn: () => disputesApi.staffComment(id, text, internal),
    onSuccess: () => (setText(''), refresh()),
  })
  const assign = useMutation({ mutationFn: () => disputesApi.assign(id), onSuccess: refresh })
  const resolve = useMutation({
    mutationFn: () => withStepUp(() => disputesApi.resolve(id, summary)),
    onSuccess: refresh,
  })
  const d = view.data?.dispute
  if (!d) {
    return <ErrorMessage error={view.error} />
  }
  return (
    <li className="space-y-2 border-t border-slate-200 pt-3">
      <p className="font-medium">
        {kindLabels[d.kind] ?? d.kind} · posao {d.dealId.slice(0, 8)} ·{' '}
        {new Date(d.openedAt).toLocaleString('sr-RS')}
      </p>
      <p>{d.description}</p>
      <Thread view={view.data!} />
      <Field
        id={`staff-comment-${id}`}
        label="Komentar"
        value={text}
        onChange={(e) => setText(e.target.value)}
      />
      <label className="block">
        <input type="checkbox" checked={internal} onChange={(e) => setInternal(e.target.checked)} />{' '}
        Interna beleška (strane je ne vide)
      </label>
      <Field
        id={`summary-${id}`}
        label="Rešenje (za zatvaranje)"
        value={summary}
        onChange={(e) => setSummary(e.target.value)}
      />
      <ErrorMessage error={comment.error ?? assign.error ?? resolve.error} />
      <div className="flex gap-2">
        <Button
          variant="secondary"
          onClick={() => comment.mutate()}
          disabled={!text.trim() || comment.isPending}
        >
          Dodaj komentar
        </Button>
        {!d.assignedTo && (
          <Button variant="secondary" onClick={() => assign.mutate()} disabled={assign.isPending}>
            Preuzmi
          </Button>
        )}
        <Button onClick={() => resolve.mutate()} disabled={!summary.trim() || resolve.isPending}>
          Reši
        </Button>
      </div>
    </li>
  )
}

/** Operations and compliance: the open disputes, automatic ones from funding discrepancies included. */
export function AdminDisputesPage() {
  const queue = useQuery({ queryKey: ['admin-disputes'], queryFn: () => disputesApi.queue() })
  return (
    <Card title="Sporovi">
      <ErrorMessage error={queue.error} />
      {queue.data?.length === 0 && <p className="text-sm">Nema otvorenih sporova.</p>}
      <ul className="space-y-4 text-sm">
        {queue.data?.map((d) => (
          <StaffDispute key={d.id} id={d.id} />
        ))}
      </ul>
    </Card>
  )
}
