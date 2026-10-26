import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useState, type FormEvent } from 'react'
import { Link, useNavigate } from 'react-router'
import { formatMoney, receivablesApi, type ImportRequest } from '../api/receivables'
import { useStepUp } from '../auth/stepUpContext'
import {
  Button,
  Card,
  EmptyRow,
  ErrorMessage,
  Field,
  PageHeader,
  Panel,
  StateChip,
} from '../components/ui'
import { buttonClass, formatDate } from '../components/styles'
import { useActiveOrganization } from '../organizations/activeOrganization'

const resultLabels: Record<string, string> = {
  Imported: 'Uvezeno',
  AlreadyImported: 'Već uvezeno',
  NotIssuer: 'Niste izdavalac',
  NotFound: 'Nije pronađeno u SEF-u',
  Unsupported: 'Nije podržano',
}

/** The seller's SEF connection (task 010): the key is checked against SEF and never shown again. */
export function SefConnectionSection({ organizationId }: { organizationId: string }) {
  const queryClient = useQueryClient()
  const withStepUp = useStepUp()
  const [apiKey, setApiKey] = useState('')
  const [pending, setPending] = useState(false)
  const connection = useQuery({
    queryKey: ['sef-connection', organizationId],
    queryFn: () => receivablesApi.sefConnection(organizationId),
  })
  const connect = useMutation({
    mutationFn: () => withStepUp(() => receivablesApi.connectSef(organizationId, apiKey)),
    onSuccess: async (updated) => {
      setApiKey('')
      if (updated.status === 'PendingConfirmation') {
        setPending(true)
        await queryClient.invalidateQueries({ queryKey: ['sef-connection', organizationId] })
      } else {
        queryClient.setQueryData(['sef-connection', organizationId], updated)
      }
      await queryClient.invalidateQueries({ queryKey: ['organization', organizationId] })
    },
  })

  function submit(event: FormEvent) {
    event.preventDefault()
    connect.mutate()
  }

  return (
    <section className="space-y-2">
      <h2 className="font-medium">SEF veza</h2>
      <p className="text-sm text-slate-600">
        Status:{' '}
        {connection.data?.status === 'Connected'
          ? 'povezano'
          : connection.data?.status === 'Invalid'
            ? 'ključ ne važi'
            : 'nije povezano'}
      </p>
      {pending && (
        <p className="text-sm">
          Novi ključ važi kada ga potvrdite preko linka poslatog na službeni email firme iz APR-a.
        </p>
      )}
      <form className="space-y-2" onSubmit={submit}>
        <Field
          id="sefKey"
          label="SEF API ključ"
          type="password"
          autoComplete="off"
          required
          value={apiKey}
          onChange={(e) => setApiKey(e.target.value)}
        />
        <ErrorMessage error={connect.error ?? connection.error} />
        <Button type="submit" variant="secondary" disabled={connect.isPending}>
          Poveži SEF
        </Button>
      </form>
      {connection.data?.status === 'Connected' && (
        <Link className="text-sm underline" to="/receivables/import">
          Uvezi fakture iz SEF-a
        </Link>
      )}
    </section>
  )
}

export function ImportPage() {
  const queryClient = useQueryClient()
  const { activeId } = useActiveOrganization()
  const [ids, setIds] = useState('')
  const [from, setFrom] = useState('')
  const [to, setTo] = useState('')
  const run = useMutation({
    mutationFn: (request: ImportRequest) => receivablesApi.importFromSef(request),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['receivables', activeId] }),
  })

  function byIds(event: FormEvent) {
    event.preventDefault()
    run.mutate({ sefInvoiceIds: ids.split(/[\s,;]+/).filter(Boolean) })
  }

  function byDates(event: FormEvent) {
    event.preventDefault()
    run.mutate({ issueDateFrom: from, issueDateTo: to })
  }

  return (
    <Card title="Uvoz faktura iz SEF-a">
      <form className="space-y-2" onSubmit={byIds}>
        <Field
          id="ids"
          label="SEF ID-jevi faktura (do 50, odvojeni zarezom)"
          value={ids}
          onChange={(e) => setIds(e.target.value)}
        />
        <Button type="submit" variant="secondary" disabled={run.isPending}>
          Uvezi po ID-ju
        </Button>
      </form>
      <form className="mt-4 grid grid-cols-2 gap-2" onSubmit={byDates}>
        <Field
          id="from"
          label="Datum izdavanja od"
          type="date"
          required
          value={from}
          onChange={(e) => setFrom(e.target.value)}
        />
        <Field
          id="to"
          label="do"
          type="date"
          required
          value={to}
          onChange={(e) => setTo(e.target.value)}
        />
        <Button type="submit" variant="secondary" className="col-span-2" disabled={run.isPending}>
          Uvezi po datumu (najviše 31 dan)
        </Button>
      </form>
      <ErrorMessage error={run.error} />
      {run.data && (
        <ul className="mt-4 space-y-1 text-sm">
          {run.data.results.map((r) => (
            <li key={r.sefInvoiceId}>
              {r.sefInvoiceId}: {resultLabels[r.status] ?? r.status}
              {r.reason && <span className="text-slate-500"> ({r.reason})</span>}
            </li>
          ))}
        </ul>
      )}
      <div className="mt-4 flex gap-4 text-sm">
        <Link className="underline" to="/receivables">
          Potraživanja
        </Link>
        <Link className="underline" to="/">
          Nazad
        </Link>
      </div>
    </Card>
  )
}

export function ReceivablesPage() {
  const navigate = useNavigate()
  const { activeId } = useActiveOrganization()
  const list = useQuery({
    queryKey: ['receivables', activeId],
    queryFn: receivablesApi.list,
    enabled: activeId !== null,
  })

  return (
    <>
      <PageHeader
        eyebrow="Prodavac"
        title="Potraživanja"
        actions={
          <Link to="/receivables/import" className={buttonClass()}>
            Uvezi iz SEF-a
          </Link>
        }
      />
      <ErrorMessage error={list.error} />
      <Panel padded={false}>
        <table>
          <thead>
            <tr>
              <th>Faktura</th>
              <th>Dužnik</th>
              <th className="text-right">Iznos</th>
              <th>Dospeće</th>
              <th>Status</th>
            </tr>
          </thead>
          <tbody>
            {list.data?.map((r) => (
              <tr
                key={r.id}
                className="cursor-pointer hover:bg-surface-2"
                onClick={() => navigate(`/receivables/${r.id}`)}
              >
                <td className="font-mono text-[13px]">
                  <Link to={`/receivables/${r.id}`} onClick={(e) => e.stopPropagation()}>
                    {r.invoice.number}
                  </Link>
                </td>
                <td>{r.debtor.legalName}</td>
                <td className="text-right font-medium">{formatMoney(r.faceValue)}</td>
                <td>{formatDate(r.maturityDate)}</td>
                <td>
                  <StateChip state={r.state} />
                </td>
              </tr>
            ))}
            {list.data?.length === 0 && (
              <EmptyRow columns={5}>Još nema uvezenih faktura. Uvezite ih iz SEF-a.</EmptyRow>
            )}
          </tbody>
        </table>
      </Panel>
    </>
  )
}
