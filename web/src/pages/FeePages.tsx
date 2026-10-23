import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { feesApi, type FeeCharge } from '../api/fees'
import { formatMoney } from '../api/receivables'
import { useStepUp } from '../auth/stepUpContext'
import { Button, Card, ErrorMessage, Field } from '../components/ui'

const statusLabels: Record<string, string> = {
  Due: 'Za naplatu',
  Invoiced: 'Fakturisano',
  Paid: 'Plaćeno',
  PendingApproval: 'Storno čeka odobrenje',
  Effective: 'Storno',
}

function currentMonth(): string {
  return new Date().toISOString().slice(0, 7)
}

function ChargeLine({ charge }: { charge: FeeCharge }) {
  return (
    <>
      {charge.kind === 'Reversal' ? 'Storno · ' : ''}
      {charge.dealReference} · faktura {charge.invoiceNumber} · {formatMoney(charge.amount)}
      {charge.vatPending ? ' (PDV naknadno)' : ` + PDV ${formatMoney(charge.vat)}`} ·{' '}
      {statusLabels[charge.status] ?? charge.status}
      {charge.dueAt && ` · dospeva ${charge.dueAt}`}
    </>
  )
}

/** The payer's platform fees and monthly statement (task 018). */
export function FeesPage() {
  const [period, setPeriod] = useState(currentMonth())
  const charges = useQuery({ queryKey: ['fee-charges'], queryFn: feesApi.mine })
  const statement = useQuery({
    queryKey: ['fee-statement', period],
    queryFn: () => feesApi.statement(period),
  })

  return (
    <Card title="Naknade platforme">
      <ul className="space-y-1 text-sm">
        {charges.data?.map((c) => (
          <li key={c.id}>
            <ChargeLine charge={c} />
          </li>
        ))}
      </ul>
      {charges.data?.length === 0 && <p className="text-sm text-slate-600">Nema naknada.</p>}
      <h2 className="mt-4 font-medium">Mesečni izveštaj</h2>
      <Field
        id="period"
        label="Mesec"
        type="month"
        value={period}
        onChange={(e) => setPeriod(e.target.value)}
      />
      {statement.data?.totals.map((t) => (
        <p key={t.total.currency} className="text-sm">
          Ukupno: {formatMoney(t.net)} + PDV {formatMoney(t.vat)} = {formatMoney(t.total)}
        </p>
      ))}
      <ErrorMessage error={charges.error ?? statement.error} />
    </Card>
  )
}

/** Finance: invoice, record payment, reverse (four-eyes) and export the month (task 018). */
export function AdminFeesPage() {
  const queryClient = useQueryClient()
  const withStepUp = useStepUp()
  const [period, setPeriod] = useState(currentMonth())
  const [reference, setReference] = useState('')
  const charges = useQuery({
    queryKey: ['admin-fee-charges', period],
    queryFn: () => feesApi.adminList(period),
  })
  const refresh = () => queryClient.invalidateQueries({ queryKey: ['admin-fee-charges', period] })
  const action = useMutation({
    mutationFn: ({
      charge,
      kind,
    }: {
      charge: FeeCharge
      kind: 'invoiced' | 'paid' | 'reverse' | 'approve'
    }) =>
      withStepUp(() => {
        switch (kind) {
          case 'invoiced':
            return feesApi.invoiced(charge.id, reference.trim())
          case 'paid':
            return feesApi.paid(charge.id, new Date().toISOString().slice(0, 10), reference.trim())
          case 'reverse':
            return feesApi.reverse(charge.id, reference.trim())
          default:
            return feesApi.approveReversal(charge.id)
        }
      }),
    onSuccess: refresh,
  })
  const exportCsv = useMutation({ mutationFn: () => feesApi.exportCsv(period) })

  return (
    <Card title="Naknade (Finance)">
      <div className="grid grid-cols-2 gap-2">
        <Field
          id="adminPeriod"
          label="Mesec"
          type="month"
          value={period}
          onChange={(e) => setPeriod(e.target.value)}
        />
        <Field
          id="reference"
          label="Broj fakture / izvoda / razlog storna"
          value={reference}
          onChange={(e) => setReference(e.target.value)}
        />
      </div>
      <Button variant="secondary" disabled={exportCsv.isPending} onClick={() => exportCsv.mutate()}>
        Izvezi CSV
      </Button>
      <ul className="mt-4 space-y-2 text-sm">
        {charges.data?.map((c) => (
          <li
            key={c.id}
            className="flex items-center justify-between gap-2 border-b border-slate-100 pb-2"
          >
            <span>
              <ChargeLine charge={c} />
            </span>
            <span className="flex gap-1">
              {c.kind === 'Charge' && c.status === 'Due' && (
                <Button onClick={() => action.mutate({ charge: c, kind: 'invoiced' })}>
                  Fakturisano
                </Button>
              )}
              {c.kind === 'Charge' && c.status === 'Invoiced' && (
                <Button onClick={() => action.mutate({ charge: c, kind: 'paid' })}>Plaćeno</Button>
              )}
              {c.kind === 'Charge' && (
                <Button
                  variant="secondary"
                  onClick={() => action.mutate({ charge: c, kind: 'reverse' })}
                >
                  Storno
                </Button>
              )}
              {c.kind === 'Reversal' && c.status === 'PendingApproval' && (
                <Button onClick={() => action.mutate({ charge: c, kind: 'approve' })}>
                  Odobri storno
                </Button>
              )}
            </span>
          </li>
        ))}
      </ul>
      <ErrorMessage error={charges.error ?? action.error ?? exportCsv.error} />
    </Card>
  )
}
