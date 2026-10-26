import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { Link, useParams } from 'react-router'
import { formatMoney, verificationApi, type CheckRow } from '../api/receivables'
import { useStepUp } from '../auth/stepUpContext'
import { Checklist, FileDrop } from '../components/inputs'
import { buttonClass, formatDate } from '../components/styles'
import { Button, Card, ErrorMessage, Field, PageHeader, Panel, StateChip } from '../components/ui'
import { PublishListingSection } from './ListingPages'

const itemLabels: Record<string, string> = {
  NoPriorAssignment: 'Potraživanje nije ranije ustupljeno niti založeno.',
  NoAssignmentProhibition: 'Ugovor sa dužnikom ne zabranjuje ustupanje.',
  NoSetOffOrDispute: 'Ne postoji prebijanje ni spor sa dužnikom.',
  GoodsOrServicesDelivered: 'Roba je isporučena, odnosno usluga izvršena.',
}

const checkLabels: Record<string, string> = {
  SELLER_IS_ISSUER: 'Prodavac je izdavalac fakture',
  SELLER_APPROVED: 'Firma prodavca je odobrena',
  SEF_FRESH_APPROVED: 'Kupac je odobrio fakturu u SEF-u (sveža provera)',
  DEBTOR_NOT_SELLER: 'Dužnik nije sam prodavac',
  DEBTOR_SCREENING: 'Provera dužnika na sankcionim listama',
  DECLARATION_COMPLETE: 'Izjava je potpuna',
  FACE_VALUE_CONSISTENT: 'Iznos odgovara fakturi',
  AMOUNT_LIMITS: 'Iznos je u dozvoljenim granicama',
  MAX_TENOR: 'Rok do dospeća je u dozvoljenim granicama',
  NOT_MATURED: 'Faktura još nije dospela',
  NEW_DEBTOR_FOR_SELLER: 'Novi dužnik za ovog prodavca',
  RELATED_PARTY: 'Provera povezanih lica',
  DUPLICATE_FINGERPRINT: 'Provera duplikata',
}

function Checks({ checks }: { checks: CheckRow[] }) {
  if (checks.length === 0) {
    return <p className="text-ink-2">Provere se pokreću kada pošaljete potraživanje na proveru.</p>
  }
  const lastRun = Math.max(...checks.map((c) => c.run))
  const marks: Record<CheckRow['result'], [string, string]> = {
    Pass: ['✓', 'text-pos'],
    Warn: ['!', 'text-warn'],
    Fail: ['✕', 'text-crit'],
    Review: ['…', 'text-ink-3'],
  }
  return (
    <ul>
      {checks
        .filter((c) => c.run === lastRun)
        .map((c) => (
          <li
            key={c.code}
            className="flex gap-2.5 border-b border-dashed border-line py-2 text-[13.5px] last:border-b-0"
          >
            <span aria-hidden className={`w-4 flex-none font-bold ${marks[c.result][1]}`}>
              {marks[c.result][0]}
            </span>
            <span>
              {checkLabels[c.code] ?? c.code}
              {c.details && <span className="block text-xs text-ink-3">{c.details}</span>}
            </span>
          </li>
        ))}
    </ul>
  )
}

const nextSteps: Record<string, [string, 'warn' | 'neutral' | 'pos']> = {
  Draft: ['Potvrdite izjavu, priložite dokument i pošaljite na proveru', 'warn'],
  PendingVerification: ['Potraživanje je na proveri; javićemo kada bude gotovo', 'neutral'],
  Verified: ['Objavite potraživanje na tržištu', 'warn'],
  Listed: ['Potraživanje je na tržištu; pratite ponude', 'pos'],
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

  const r = receivable.data
  if (!r) {
    return (
      <>
        <PageHeader eyebrow="Potraživanje" title="Potraživanje" />
        <ErrorMessage error={receivable.error} />
      </>
    )
  }

  const items = declaration.data?.items ?? []
  const next = nextSteps[r.state]
  return (
    <>
      <div className="flex gap-1.5 text-[12.5px] text-ink-3">
        <Link to="/receivables" className="text-ink-2 underline underline-offset-2">
          Potraživanja
        </Link>
        <span>/</span>
        <span>{r.invoice.number}</span>
      </div>
      <PageHeader
        eyebrow={`Faktura ${r.invoice.number} · SEF ${r.invoice.sefInvoiceId}`}
        title={`${r.debtor.legalName} · ${formatMoney(r.faceValue)}`}
        actions={<StateChip state={r.state} />}
      />
      {next && (
        <div
          className={`flex flex-wrap items-center justify-between gap-3 rounded-lg border px-[18px] py-3 ${
            next[1] === 'warn'
              ? 'border-warn bg-warn-soft'
              : next[1] === 'pos'
                ? 'border-pos bg-pos-soft'
                : 'border-line bg-surface'
          }`}
        >
          <span>
            <b>Sledeći korak:</b> {next[0]}
          </span>
          {r.state === 'Listed' && (
            <Link to="/listings" className={buttonClass('secondary', 'sm')}>
              Oglasi i ponude
            </Link>
          )}
        </div>
      )}
      {submit.data?.reviewRequired && (
        <p className="rounded-md bg-surface-2 px-3 py-2.5 text-[13px] text-ink-2">
          Potraživanje čeka ručni pregled službe za usklađenost.
        </p>
      )}
      <div className="grid items-start gap-6 lg:grid-cols-[minmax(0,1.6fr)_minmax(0,1fr)]">
        <div className="flex flex-col gap-4">
          {r.state === 'Draft' && (
            <Panel title="Priprema za proveru">
              <div className="flex flex-col gap-4">
                <Checklist
                  title={`Izjava uz fakturu (${declaration.data?.version ?? ''})`}
                  items={items.map((item) => ({ key: item, label: itemLabels[item] ?? item }))}
                  values={answers}
                  onChange={(key, checked) => setAnswers({ ...answers, [key]: checked })}
                />
                <div className="flex flex-wrap items-center gap-3">
                  <Button
                    variant="secondary"
                    size="sm"
                    onClick={() => save.mutate()}
                    disabled={save.isPending || items.length === 0}
                  >
                    Sačuvaj izjavu
                  </Button>
                  {save.data?.blocking && (
                    <span className="text-[13px] text-warn">{save.data.blocking}</span>
                  )}
                  {save.data?.complete && (
                    <span className="text-[13px] text-pos">Izjava je potpuna.</span>
                  )}
                </div>
                <ErrorMessage error={save.error} />
                <FileDrop
                  label="Prateći dokument (ugovor, porudžbenica, otpremnica)"
                  hint="PDF, slika ili XML"
                  accept=".pdf,.png,.jpg,.jpeg,.xml"
                  onFile={(file) => attach.mutateAsync(file)}
                />
                <ErrorMessage error={attach.error} />
                <div className="flex flex-wrap gap-2 border-t border-line pt-4">
                  <Button onClick={() => submit.mutate()} disabled={submit.isPending}>
                    Pošalji na proveru
                  </Button>
                  <Button
                    variant="secondary"
                    onClick={() => cancel.mutate()}
                    disabled={cancel.isPending}
                  >
                    Otkaži potraživanje
                  </Button>
                </div>
                <ErrorMessage error={submit.error ?? cancel.error} />
              </div>
            </Panel>
          )}
          {r.state === 'Verified' && (
            <Panel title="Objavi na tržištu">
              <PublishListingSection receivableId={r.id} />
            </Panel>
          )}
          <Panel title="Faktura">
            <dl className="grid grid-cols-[repeat(auto-fit,minmax(150px,1fr))] gap-x-5 gap-y-3.5">
              {(
                [
                  ['Izdata', formatDate(r.invoice.issueDate)],
                  ['Dospeva', formatDate(r.invoice.dueDate)],
                  ['Iznos (bruto)', formatMoney(r.invoice.gross)],
                  ['Dužnik', r.debtor.legalName],
                  ['PIB dužnika', r.debtor.pib],
                  ['SEF status', r.invoice.sefStatus],
                ] as [string, string][]
              ).map(([label, value]) => (
                <div key={label} className="min-w-0">
                  <dt className="mb-0.5 text-xs text-ink-3">{label}</dt>
                  <dd className="font-medium break-words">{value}</dd>
                </div>
              ))}
            </dl>
          </Panel>
        </div>
        <Panel title="Provere">
          <Checks checks={checks.data ?? []} />
        </Panel>
      </div>
    </>
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
