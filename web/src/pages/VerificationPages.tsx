import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useState, type ChangeEvent } from 'react'
import { Link, useParams } from 'react-router'
import { formatMoney, verificationApi, type CheckRow } from '../api/receivables'
import { useStepUp } from '../auth/stepUpContext'
import { Button, Card, ErrorMessage, Field } from '../components/ui'

const itemLabels: Record<string, string> = {
  NoPriorAssignment: 'Potraživanje nije ranije ustupljeno niti založeno.',
  NoAssignmentProhibition: 'Ugovor sa dužnikom ne zabranjuje ustupanje.',
  NoSetOffOrDispute: 'Ne postoji prebijanje ni spor sa dužnikom.',
  GoodsOrServicesDelivered: 'Roba je isporučena, odnosno usluga izvršena.',
}

const stateLabels: Record<string, string> = {
  Draft: 'Nacrt',
  PendingVerification: 'Na proveri',
  Verified: 'Provereno',
  Rejected: 'Odbijeno',
  Cancelled: 'Otkazano',
}

function Checks({ checks }: { checks: CheckRow[] }) {
  if (checks.length === 0) {
    return null
  }
  const lastRun = Math.max(...checks.map((c) => c.run))
  return (
    <ul className="space-y-1 text-sm">
      {checks
        .filter((c) => c.run === lastRun)
        .map((c) => (
          <li key={c.code}>
            <span
              className={
                c.result === 'Fail'
                  ? 'text-red-700'
                  : c.result === 'Warn'
                    ? 'text-amber-700'
                    : c.result === 'Review'
                      ? 'text-slate-600'
                      : 'text-green-700'
              }
            >
              {c.result}
            </span>{' '}
            {c.code}
            {c.details && <span className="text-slate-500"> — {c.details}</span>}
          </li>
        ))}
    </ul>
  )
}

/** The seller's receivable: declaration, supporting document, submit, and the check results (task 011). */
export function ReceivableDetailPage() {
  const { id = '' } = useParams()
  const queryClient = useQueryClient()
  const receivable = useQuery({
    queryKey: ['receivable', id],
    queryFn: () => verificationApi.get(id),
  })
  const declaration = useQuery({ queryKey: ['declaration'], queryFn: verificationApi.declaration })
  const checks = useQuery({ queryKey: ['checks', id], queryFn: () => verificationApi.checks(id) })
  const [answers, setAnswers] = useState<Record<string, boolean>>({})
  const refresh = () =>
    Promise.all([
      queryClient.invalidateQueries({ queryKey: ['receivable', id] }),
      queryClient.invalidateQueries({ queryKey: ['checks', id] }),
    ])
  const save = useMutation({ mutationFn: () => verificationApi.saveDeclaration(id, answers) })
  const attach = useMutation({
    mutationFn: async (file: File) =>
      verificationApi.attach(id, (await verificationApi.uploadEvidence(file)).id),
  })
  const submit = useMutation({ mutationFn: () => verificationApi.submit(id), onSuccess: refresh })
  const cancel = useMutation({ mutationFn: () => verificationApi.cancel(id), onSuccess: refresh })

  function onFile(event: ChangeEvent<HTMLInputElement>) {
    const file = event.target.files?.[0]
    if (file) {
      attach.mutate(file)
    }
  }

  const r = receivable.data
  if (!r) {
    return (
      <Card title="Potraživanje">
        <ErrorMessage error={receivable.error} />
      </Card>
    )
  }

  const items = declaration.data?.items ?? []
  return (
    <Card title={`Faktura ${r.invoice.number}`}>
      <p className="mb-4 text-sm">
        {r.debtor.legalName} · {formatMoney(r.faceValue)} · dospeće {r.maturityDate} · status:{' '}
        {stateLabels[r.state] ?? r.state}
      </p>
      {r.state === 'Draft' && (
        <div className="space-y-4">
          <section className="space-y-2">
            <h2 className="font-medium">Izjava ({declaration.data?.version})</h2>
            {items.map((item) => (
              <label key={item} className="flex items-start gap-2 text-sm">
                <input
                  type="checkbox"
                  checked={answers[item] ?? false}
                  onChange={(e) => setAnswers({ ...answers, [item]: e.target.checked })}
                />
                {itemLabels[item] ?? item}
              </label>
            ))}
            <Button
              variant="secondary"
              onClick={() => save.mutate()}
              disabled={save.isPending || items.length === 0}
            >
              Sačuvaj izjavu
            </Button>
            {save.data?.blocking && <p className="text-sm text-amber-700">{save.data.blocking}</p>}
            {save.data?.complete && <p className="text-sm">Izjava je potpuna.</p>}
            <ErrorMessage error={save.error} />
          </section>
          <section className="space-y-2">
            <h2 className="font-medium">Prateći dokument (ugovor, porudžbenica, otpremnica)</h2>
            <input
              aria-label="Prateći dokument"
              type="file"
              accept=".pdf,.png,.jpg,.jpeg,.xml"
              onChange={onFile}
            />
            {attach.isSuccess && <p className="text-sm">Dokument je priložen.</p>}
            <ErrorMessage error={attach.error} />
          </section>
          <div className="flex gap-2">
            <Button onClick={() => submit.mutate()} disabled={submit.isPending}>
              Pošalji na proveru
            </Button>
            <Button variant="secondary" onClick={() => cancel.mutate()} disabled={cancel.isPending}>
              Otkaži
            </Button>
          </div>
          <ErrorMessage error={submit.error ?? cancel.error} />
        </div>
      )}
      {submit.data?.reviewRequired && (
        <p className="mt-2 text-sm">Potraživanje čeka ručni pregled službe za usklađenost.</p>
      )}
      <section className="mt-4 space-y-2">
        <h2 className="font-medium">Provere</h2>
        <Checks checks={checks.data ?? []} />
      </section>
      <Link to="/receivables" className="mt-4 block text-sm underline">
        Nazad
      </Link>
    </Card>
  )
}

/** The compliance review queue (task 011); visible only to compliance staff. */
export function ReviewQueuePage() {
  const queryClient = useQueryClient()
  const withStepUp = useStepUp()
  const [reason, setReason] = useState('')
  const queue = useQuery({ queryKey: ['review-queue'], queryFn: verificationApi.queue })
  const decide = useMutation({
    mutationFn: ({ id, verified }: { id: string; verified: boolean }) =>
      withStepUp(() =>
        verificationApi.decide(
          id,
          verified ? 'Verified' : 'Rejected',
          verified ? null : reason,
          '',
        ),
      ),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['review-queue'] }),
  })

  return (
    <Card title="Ručni pregled potraživanja">
      <Field
        id="reason"
        label="Razlog odbijanja (npr. Fraud, WrongDocuments)"
        value={reason}
        onChange={(e) => setReason(e.target.value)}
      />
      <ul className="mt-4 space-y-2 text-sm">
        {queue.data?.map((q) => (
          <li
            key={q.id}
            className="flex items-center justify-between gap-2 border-b border-slate-100 pb-2"
          >
            <span>
              {q.id.slice(0, 8)} · dospeće {q.maturityDate}
            </span>
            <span className="flex gap-2">
              <Button onClick={() => decide.mutate({ id: q.id, verified: true })}>Odobri</Button>
              <Button
                variant="secondary"
                disabled={reason.trim() === ''}
                onClick={() => decide.mutate({ id: q.id, verified: false })}
              >
                Odbij
              </Button>
            </span>
          </li>
        ))}
      </ul>
      {queue.data?.length === 0 && <p className="text-sm text-slate-600">Red je prazan.</p>}
      <ErrorMessage error={queue.error ?? decide.error} />
    </Card>
  )
}
