import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useState, type FormEvent } from 'react'
import { Link } from 'react-router'
import { formatMoney, receivablesApi, type ImportRequest } from '../api/receivables'
import { useStepUp } from '../auth/stepUpContext'
import { Button, Card, ErrorMessage, Field } from '../components/ui'
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
  const connection = useQuery({
    queryKey: ['sef-connection', organizationId],
    queryFn: () => receivablesApi.sefConnection(organizationId),
  })
  const connect = useMutation({
    mutationFn: () => withStepUp(() => receivablesApi.connectSef(organizationId, apiKey)),
    onSuccess: (updated) => {
      setApiKey('')
      queryClient.setQueryData(['sef-connection', organizationId], updated)
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
  const { activeId } = useActiveOrganization()
  const list = useQuery({
    queryKey: ['receivables', activeId],
    queryFn: receivablesApi.list,
    enabled: activeId !== null,
  })

  return (
    <Card title="Potraživanja">
      <table className="w-full text-left text-sm">
        <thead>
          <tr className="text-slate-500">
            <th className="py-1">Faktura</th>
            <th>Dužnik</th>
            <th>Iznos</th>
            <th>Dospeće</th>
            <th>Status</th>
          </tr>
        </thead>
        <tbody>
          {list.data?.map((r) => (
            <tr key={r.id} className="border-t border-slate-100">
              <td className="py-1">
                <Link className="underline" to={`/receivables/${r.id}`}>
                  {r.invoice.number}
                </Link>
              </td>
              <td>{r.debtor.legalName}</td>
              <td>{formatMoney(r.faceValue)}</td>
              <td>{r.maturityDate}</td>
              <td>{r.state === 'Draft' ? 'Nacrt' : r.state}</td>
            </tr>
          ))}
        </tbody>
      </table>
      {list.data?.length === 0 && (
        <p className="mt-2 text-sm text-slate-600">Još nema uvezenih faktura.</p>
      )}
      <ErrorMessage error={list.error} />
      <div className="mt-4 flex gap-4 text-sm">
        <Link className="underline" to="/receivables/import">
          Uvezi iz SEF-a
        </Link>
        <Link className="underline" to="/">
          Nazad
        </Link>
      </div>
    </Card>
  )
}
