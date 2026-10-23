import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useState, type ChangeEvent } from 'react'
import { dealsApi, type DealDetail } from '../api/deals'
import { formatMoney } from '../api/receivables'
import { useStepUp } from '../auth/stepUpContext'
import { Button, ErrorMessage, Field } from '../components/ui'

const cefLabels: Record<string, string> = {
  NotApplicable: 'Nije primenljivo (CEF ne radi)',
  Pending: 'Čeka upis u CEF',
  Registered: 'Upisano u CEF',
}

function today(): string {
  return new Date().toISOString().slice(0, 10)
}

/** The deal after signing (task 017): CEF, debtor notification, funding, confirmation and outcome. */
export function DealExecution({ detail }: { detail: DealDetail }) {
  const queryClient = useQueryClient()
  const withStepUp = useStepUp()
  const { deal, steps, myRole } = detail
  const [paymentReference, setPaymentReference] = useState('')
  const [amount, setAmount] = useState(deal.purchasePrice.amount)
  const [valueDate, setValueDate] = useState(today())
  const [cefReference, setCefReference] = useState('')
  const [reason, setReason] = useState('')
  const [outcomeType, setOutcomeType] = useState('Settled')
  const [collected, setCollected] = useState('')
  const refresh = () =>
    Promise.all([
      queryClient.invalidateQueries({ queryKey: ['deal', deal.id] }),
      queryClient.invalidateQueries({ queryKey: ['deal-timeline', deal.id] }),
    ])
  const accounts = useQuery({
    queryKey: ['funding-accounts', deal.id],
    queryFn: () => dealsApi.fundingAccounts(deal.id),
    enabled: myRole === 'Factor' && deal.state === 'Signed',
  })
  const report = useMutation({
    mutationFn: (payeeBankAccountId: string) =>
      withStepUp(() =>
        dealsApi.reportFunding(deal.id, {
          amount: { amount: amount.trim(), currency: deal.purchasePrice.currency },
          valueDate,
          paymentReference: paymentReference.trim(),
          payeeBankAccountId,
        }),
      ),
    onSuccess: refresh,
  })
  const confirm = useMutation({
    mutationFn: (received: boolean) =>
      withStepUp(() => dealsApi.confirmFunding(deal.id, received, reason.trim() || null)),
    onSuccess: refresh,
  })
  const outcome = useMutation({
    mutationFn: () =>
      withStepUp(() =>
        dealsApi.outcome(
          deal.id,
          outcomeType,
          today(),
          collected.trim() ? { amount: collected.trim(), currency: deal.faceValue.currency } : null,
        ),
      ),
    onSuccess: refresh,
  })
  const cef = useMutation({
    mutationFn: async (file: File) =>
      dealsApi.cefRegistration(
        deal.id,
        cefReference.trim(),
        (await dealsApi.uploadEvidence(file)).id,
      ),
    onSuccess: refresh,
  })
  const notify = useMutation({
    mutationFn: async (file: File) =>
      dealsApi.debtorNotification(
        deal.id,
        'Email',
        new Date().toISOString(),
        (await dealsApi.uploadEvidence(file)).id,
      ),
    onSuccess: refresh,
  })

  if (!['Signed', 'FundingReported', 'Funded', 'Closed'].includes(deal.state)) {
    return null
  }

  function onFile(mutate: (file: File) => void) {
    return (event: ChangeEvent<HTMLInputElement>) => {
      const file = event.target.files?.[0]
      if (file) {
        mutate(file)
      }
    }
  }

  return (
    <section className="mt-4 space-y-3 text-sm">
      {steps.openDisputes.length > 0 && (
        <p className="rounded bg-red-50 p-2 text-red-900">
          Otvoren spor ({steps.openDisputes.join(', ')}): posao je blokiran do odluke platforme.
        </p>
      )}
      {steps.cef && (
        <p>
          CEF: {cefLabels[steps.cef.status] ?? steps.cef.status}
          {steps.cef.note && ` · ${steps.cef.note}`}
          {steps.cef.reference && ` · ${steps.cef.reference}`}
        </p>
      )}
      {steps.cef?.status === 'Pending' && myRole === 'Seller' && (
        <div className="space-y-2">
          <Field
            id="cefReference"
            label="Broj upisa u CEF"
            value={cefReference}
            onChange={(e) => setCefReference(e.target.value)}
          />
          <input aria-label="Dokaz o upisu u CEF" type="file" onChange={onFile(cef.mutate)} />
        </div>
      )}
      {deal.state !== 'Closed' && (
        <div className="space-y-1">
          <p>Obaveštenje dužniku: {steps.debtorNotified ? 'evidentirano' : 'nije evidentirano'}</p>
          <input
            aria-label="Dokaz o obaveštenju dužnika"
            type="file"
            onChange={onFile(notify.mutate)}
          />
        </div>
      )}
      {deal.state === 'Signed' && myRole === 'Factor' && (
        <div className="space-y-2">
          <h2 className="font-medium">Prijava isplate</h2>
          <Field
            id="amount"
            label="Isplaćen iznos"
            inputMode="decimal"
            value={amount}
            onChange={(e) => setAmount(e.target.value)}
          />
          <Field
            id="valueDate"
            label="Datum valute"
            type="date"
            value={valueDate}
            onChange={(e) => setValueDate(e.target.value)}
          />
          <Field
            id="paymentReference"
            label="Poziv na broj / referenca plaćanja"
            value={paymentReference}
            onChange={(e) => setPaymentReference(e.target.value)}
          />
          {accounts.data?.map((a) => (
            <div key={a.id} className="flex items-center gap-2">
              <span>
                {a.bankName}: {a.accountNumber}
              </span>
              <Button
                disabled={report.isPending || paymentReference.trim() === ''}
                onClick={() => report.mutate(a.id)}
              >
                Prijavi isplatu na ovaj račun
              </Button>
            </div>
          ))}
          <ErrorMessage error={accounts.error} />
        </div>
      )}
      {deal.state === 'FundingReported' && steps.fundingReport && (
        <div className="space-y-2">
          <p>
            Faktor je prijavio isplatu {formatMoney(steps.fundingReport.amount)} (valuta{' '}
            {steps.fundingReport.valueDate}, referenca {steps.fundingReport.paymentReference}).
          </p>
          {myRole === 'Seller' && (
            <>
              <Field
                id="fundingReason"
                label="Ako uplata nije stigla ili se razlikuje: šta nije u redu"
                value={reason}
                onChange={(e) => setReason(e.target.value)}
              />
              <div className="flex gap-2">
                <Button disabled={confirm.isPending} onClick={() => confirm.mutate(true)}>
                  Potvrđujem prijem uplate
                </Button>
                <Button
                  variant="secondary"
                  disabled={confirm.isPending || reason.trim() === ''}
                  onClick={() => confirm.mutate(false)}
                >
                  Uplata nije u redu
                </Button>
              </div>
            </>
          )}
        </div>
      )}
      {deal.state === 'Funded' && myRole === 'Factor' && (
        <div className="space-y-2">
          <h2 className="font-medium">Ishod</h2>
          <select
            aria-label="Ishod"
            className="block w-full rounded border border-slate-300 px-2 py-1"
            value={outcomeType}
            onChange={(e) => setOutcomeType(e.target.value)}
          >
            <option value="Settled">Dužnik je platio</option>
            <option value="RecourseExercised">Regres prema prodavcu</option>
            <option value="Defaulted">Nenaplativo (bez regresa)</option>
          </select>
          <Field
            id="collected"
            label="Naplaćen iznos (opciono)"
            inputMode="decimal"
            value={collected}
            onChange={(e) => setCollected(e.target.value)}
          />
          <Button disabled={outcome.isPending} onClick={() => outcome.mutate()}>
            Evidentiraj ishod
          </Button>
        </div>
      )}
      <ErrorMessage
        error={report.error ?? confirm.error ?? outcome.error ?? cef.error ?? notify.error}
      />
    </section>
  )
}
