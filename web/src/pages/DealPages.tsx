import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useState, type ChangeEvent } from 'react'
import { Link, useNavigate, useParams } from 'react-router'
import { download } from '../api/client'
import { dealsApi, type DealDetail } from '../api/deals'
import { formatMoney } from '../api/receivables'
import { useStepUp } from '../auth/stepUpContext'
import { Button, Chip, EmptyRow, ErrorMessage, Field, PageHeader, Panel } from '../components/ui'
import { DealExecution } from './DealExecution'
import { DealDisputes } from './DisputePages'
import { useActiveOrganization } from '../organizations/activeOrganization'

const dealStates: Record<string, string> = {
  AwaitingFactorCdd: 'Čeka proveru faktora (CDD)',
  AwaitingContract: 'Čeka ugovor',
  AwaitingSignatures: 'Čeka potpise',
  Signed: 'Potpisano',
  FundingReported: 'Isplata prijavljena',
  Funded: 'Isplaćeno',
  Closed: 'Zatvoreno',
  Cancelled: 'Otkazano',
  Terminated: 'Raskinuto',
}

const signatureResults: Record<string, string> = {
  Valid: 'Ispravan potpis',
  signature_invalid: 'Potpis nije ispravan',
  certificate_not_qualified: 'Sertifikat nije kvalifikovan',
  certificate_untrusted: 'Sertifikat nije od pouzdanog izdavaoca',
  certificate_revoked: 'Sertifikat je opozvan',
  document_mismatch: 'Potpisan je drugi dokument',
  signer_not_authorized: 'Potpisnik nije zastupnik',
}

function dateTime(value: string): string {
  return new Date(value).toLocaleString('sr-Latn')
}

/** The deals of the active organization, as seller or factor (task 014). */
export function DealsPage() {
  const navigate = useNavigate()
  const deals = useQuery({ queryKey: ['deals'], queryFn: dealsApi.list })
  return (
    <>
      <PageHeader eyebrow="Poslovi" title="Poslovi" />
      <ErrorMessage error={deals.error} />
      <Panel padded={false}>
        <table>
          <thead>
            <tr>
              <th>Referenca</th>
              <th className="text-right">Nominalni iznos</th>
              <th className="text-right">Kupoprodajna cena</th>
              <th>Regres</th>
              <th>Status</th>
            </tr>
          </thead>
          <tbody>
            {deals.data?.map((d) => (
              <tr
                key={d.id}
                className="cursor-pointer hover:bg-surface-2"
                onClick={() => navigate(`/deals/${d.id}`)}
              >
                <td className="font-mono text-[13px]">
                  <Link to={`/deals/${d.id}`} onClick={(e) => e.stopPropagation()}>
                    {d.reference}
                  </Link>
                </td>
                <td className="text-right">{formatMoney(d.faceValue)}</td>
                <td className="text-right font-medium">{formatMoney(d.purchasePrice)}</td>
                <td>{d.recourseType === 'Recourse' ? 'Sa regresom' : 'Bez regresa'}</td>
                <td>
                  <Chip tone={dealTone(d.state)}>{dealStates[d.state] ?? d.state}</Chip>
                </td>
              </tr>
            ))}
            {deals.data?.length === 0 && (
              <EmptyRow columns={5}>Još nema poslova. Posao nastaje prihvatanjem ponude.</EmptyRow>
            )}
          </tbody>
        </table>
      </Panel>
    </>
  )
}

/** The steps before signing (task 015): CDD, agreement, terms checklist and cancellation. */
function DealSteps({ detail }: { detail: DealDetail }) {
  const queryClient = useQueryClient()
  const withStepUp = useStepUp()
  const [reason, setReason] = useState('')
  const { deal, steps, myRole } = detail
  const refresh = () =>
    Promise.all([
      queryClient.invalidateQueries({ queryKey: ['deal', deal.id] }),
      queryClient.invalidateQueries({ queryKey: ['deal-timeline', deal.id] }),
    ])
  const cdd = useMutation({
    mutationFn: (approved: boolean) =>
      withStepUp(() =>
        dealsApi.cddDecision(deal.id, approved ? 'Approved' : 'Declined', reason.trim() || null),
      ),
    onSuccess: refresh,
  })
  const contract = useMutation({
    mutationFn: async (file: File) =>
      dealsApi.attachContract(deal.id, (await dealsApi.uploadContract(file)).id),
    onSuccess: refresh,
  })
  const confirm = useMutation({
    mutationFn: (contractDocumentId: string) =>
      withStepUp(() => dealsApi.confirmTerms(deal.id, contractDocumentId)),
    onSuccess: refresh,
  })
  const cancel = useMutation({
    mutationFn: () =>
      withStepUp(() => dealsApi.cancel(deal.id, 'PartyCancelled', reason.trim() || null)),
    onSuccess: refresh,
  })
  const signed = useMutation({
    mutationFn: (file: File) =>
      withStepUp(async () =>
        dealsApi.signedContract(deal.id, (await dealsApi.uploadContract(file)).id),
      ),
    onSuccess: refresh,
  })
  const fetchContract = useMutation({
    mutationFn: (documentId: string) => download(`/documents/${documentId}/content`, 'ugovor.pdf'),
  })
  const beforeSigning = ['AwaitingFactorCdd', 'AwaitingContract', 'AwaitingSignatures'].includes(
    deal.state,
  )
  const iConfirmed = myRole === 'Seller' ? steps.sellerConfirmed : steps.factorConfirmed
  const current = steps.contract

  function onSignedFile(event: ChangeEvent<HTMLInputElement>) {
    const file = event.target.files?.[0]
    if (file) {
      signed.mutate(file)
    }
  }

  function onFile(event: ChangeEvent<HTMLInputElement>) {
    const file = event.target.files?.[0]
    if (file) {
      contract.mutate(file)
    }
  }

  return (
    <section className="space-y-3 text-sm">
      {beforeSigning && (
        <Field
          id="reason"
          label="Razlog (za odbijanje CDD-a ili otkazivanje)"
          value={reason}
          onChange={(e) => setReason(e.target.value)}
        />
      )}
      {deal.state === 'AwaitingFactorCdd' && myRole === 'Factor' && (
        <div className="space-y-2">
          <h2 className="font-medium">CDD provera prodavca</h2>
          <p>KYB paket prodavca biće dostupan kada prodavac da saglasnost za deljenje (KYB).</p>
          <div className="flex gap-2">
            <Button disabled={cdd.isPending} onClick={() => cdd.mutate(true)}>
              CDD odobren
            </Button>
            <Button
              variant="secondary"
              disabled={cdd.isPending || reason.trim() === ''}
              onClick={() => cdd.mutate(false)}
            >
              Odbij (otkazuje posao)
            </Button>
          </div>
        </div>
      )}
      {deal.state === 'AwaitingContract' && (
        <div className="space-y-2">
          <h2 className="font-medium">Ugovor o faktoringu</h2>
          {myRole === 'Factor' && (
            <input aria-label="Ugovor (PDF)" type="file" accept=".pdf" onChange={onFile} />
          )}
          {current ? (
            <>
              <button
                type="button"
                className="underline"
                onClick={() => fetchContract.mutate(current.documentId)}
              >
                Preuzmi trenutnu verziju ugovora
              </button>
              <p>
                Proverite u ugovoru: strane i PIB, broj fakture, iznos potraživanja{' '}
                {formatMoney(deal.faceValue)}, kupoprodajnu cenu {formatMoney(deal.purchasePrice)},{' '}
                {deal.recourseType === 'Recourse' ? 'sa regresom' : 'bez regresa'}, rok isplate{' '}
                {deal.fundingDeadlineBusinessDays} radnih dana posle potpisa.
              </p>
              <p>
                Prodavac {steps.sellerConfirmed ? 'je potvrdio' : 'nije potvrdio'} · faktor{' '}
                {steps.factorConfirmed ? 'je potvrdio' : 'nije potvrdio'}
              </p>
              {!iConfirmed && (
                <Button
                  disabled={confirm.isPending}
                  onClick={() => confirm.mutate(current.contractDocumentId)}
                >
                  Potvrđujem da ugovor odgovara uslovima posla
                </Button>
              )}
            </>
          ) : (
            <p>Faktor još nije otpremio ugovor.</p>
          )}
        </div>
      )}
      {deal.state === 'AwaitingSignatures' && (
        <div className="space-y-2">
          <h2 className="font-medium">Potpisivanje</h2>
          <p>
            Potpišite usaglašeni ugovor kvalifikovanim elektronskim potpisom zastupnika i otpremite
            potpisani PDF. Posao je potpisan kada obe strane imaju bar jedan ispravan potpis.
          </p>
          <input
            aria-label="Potpisani ugovor (PDF)"
            type="file"
            accept=".pdf"
            onChange={onSignedFile}
          />
        </div>
      )}
      {steps.signatures.length > 0 && (
        <ul className="space-y-1">
          {steps.signatures.map((s) => (
            <li key={`${s.signer}-${s.validatedAt}`}>
              <span className={s.result === 'Valid' ? 'text-green-700' : 'text-red-700'}>
                {signatureResults[s.result] ?? s.result}
              </span>{' '}
              · {s.signer}
              {s.party && ` (${s.party === 'Seller' ? 'prodavac' : 'faktor'})`}
            </li>
          ))}
        </ul>
      )}
      {beforeSigning && (
        <Button
          variant="secondary"
          disabled={cancel.isPending || reason.trim() === ''}
          onClick={() => cancel.mutate()}
        >
          Otkaži posao
        </Button>
      )}
      <ErrorMessage
        error={
          cdd.error ??
          contract.error ??
          confirm.error ??
          cancel.error ??
          signed.error ??
          fetchContract.error
        }
      />
    </section>
  )
}

/** The visual order of a deal (ux-design.md S8); the actions of the current step come from the deal itself. */
const dealSteps: {
  state: string
  title: string
  who: 'Seller' | 'Factor' | 'Both'
  text: string
}[] = [
  {
    state: 'AwaitingFactorCdd',
    title: 'Provera prodavca (CDD)',
    who: 'Factor',
    text: 'Faktor proverava prodavca po svojim pravilima i odobrava posao.',
  },
  {
    state: 'AwaitingContract',
    title: 'Ugovor o faktoringu',
    who: 'Both',
    text: 'Faktor otprema ugovor, obe strane potvrđuju da odgovara uslovima posla.',
  },
  {
    state: 'AwaitingSignatures',
    title: 'Potpisivanje',
    who: 'Both',
    text: 'Zastupnici potpisuju ugovor kvalifikovanim elektronskim potpisom.',
  },
  {
    state: 'Signed',
    title: 'Isplata',
    who: 'Factor',
    text: 'Faktor isplaćuje kupoprodajnu cenu na verifikovan račun prodavca i prijavljuje isplatu.',
  },
  {
    state: 'FundingReported',
    title: 'Potvrda prijema',
    who: 'Seller',
    text: 'Prodavac potvrđuje da je novac stigao.',
  },
  {
    state: 'Funded',
    title: 'Ishod naplate',
    who: 'Factor',
    text: 'Faktor upisuje ishod kada dužnik plati (ili ne plati).',
  },
]

const whoLabel = { Seller: 'prodavac', Factor: 'faktor', Both: 'obe strane' }

function dealTone(state: string): 'neutral' | 'accent' | 'pos' | 'warn' | 'crit' {
  if (state === 'Closed' || state === 'Funded') return 'pos'
  if (state === 'Cancelled' || state === 'Terminated') return 'crit'
  return 'warn'
}

/** A deal with its terms, the steps and the timeline (tasks 014, 015, 017). */
export function DealPage() {
  const { activeId } = useActiveOrganization()
  const { id = '' } = useParams()
  const detail = useQuery({ queryKey: ['deal', id], queryFn: () => dealsApi.get(id) })
  const timeline = useQuery({
    queryKey: ['deal-timeline', id],
    queryFn: () => dealsApi.timeline(id),
  })
  const d = detail.data?.deal
  if (!detail.data || !d) {
    return (
      <>
        <PageHeader eyebrow="Posao" title="Posao" />
        <ErrorMessage error={detail.error} />
      </>
    )
  }

  const { steps, myRole } = detail.data
  const currentIndex = dealSteps.findIndex((s) => s.state === d.state)
  const ended = d.state === 'Closed' || d.state === 'Cancelled' || d.state === 'Terminated'
  const current = currentIndex >= 0 ? dealSteps[currentIndex] : null
  const myTurn =
    current !== null &&
    (current.who === 'Both' || current.who === myRole) &&
    (steps.waitingFor === null || steps.waitingFor === 'Both' || steps.waitingFor === myRole)

  return (
    <>
      <div className="flex gap-1.5 text-[12.5px] text-ink-3">
        <Link to="/deals" className="text-ink-2 underline underline-offset-2">
          Poslovi
        </Link>
        <span>/</span>
        <span className="font-mono">{d.reference}</span>
      </div>
      <PageHeader
        eyebrow={`Posao ${d.reference}`}
        title={formatMoney(d.faceValue)}
        actions={<Chip tone={dealTone(d.state)}>{dealStates[d.state] ?? d.state}</Chip>}
      />
      {ended ? (
        <div className="rounded-lg border border-pos bg-pos-soft px-[18px] py-3.5">
          <b>Posao je {dealStates[d.state]?.toLowerCase() ?? d.state}.</b>
        </div>
      ) : (
        current && (
          <div
            className={`flex flex-wrap items-center justify-between gap-3.5 rounded-lg border px-[18px] py-3.5 ${
              myTurn ? 'border-warn bg-warn-soft' : 'border-line bg-surface'
            }`}
          >
            <div>
              <b>{myTurn ? 'Na potezu ste vi:' : `Na potezu je ${whoLabel[current.who]}:`}</b>{' '}
              {current.title}
              {d.state === 'AwaitingSignatures' && (
                <>
                  {' '}
                  · rok za potpise <b>{dateTime(d.signatureDeadline)}</b>
                </>
              )}
            </div>
            {!myTurn && (
              <span className="text-[12.5px] text-ink-2">Obavestićemo vas kada bude vaš red.</span>
            )}
          </div>
        )
      )}
      <div className="grid items-start gap-6 lg:grid-cols-[minmax(0,1.6fr)_minmax(0,1fr)]">
        <div className="flex flex-col gap-4">
          <Panel
            title="Koraci"
            actions={<span className="text-[12.5px] text-ink-3">Redosled je obavezan</span>}
            padded={false}
          >
            <ol>
              {dealSteps.map((s, index) => {
                const done = ended
                  ? d.state === 'Closed' || index < currentIndex
                  : index < currentIndex
                const isCurrent = index === currentIndex && !ended
                return (
                  <li
                    key={s.state}
                    className={`grid grid-cols-[30px_minmax(0,1fr)] gap-3.5 border-b border-line px-[18px] py-3.5 last:border-b-0 ${
                      isCurrent ? 'bg-accent-soft/35' : ''
                    }`}
                  >
                    <div
                      aria-hidden
                      className={`grid size-[26px] place-items-center rounded-full border-[1.5px] text-xs font-semibold tabular-nums ${
                        done
                          ? 'border-pos bg-pos text-surface'
                          : isCurrent
                            ? 'border-accent text-accent ring-4 ring-accent-soft'
                            : 'border-line bg-surface text-ink-3'
                      }`}
                    >
                      {done ? '✓' : index + 1}
                    </div>
                    <div>
                      <div>
                        <span className="font-semibold">{s.title}</span>
                        <span className="ml-2 text-xs font-medium text-ink-3">
                          {whoLabel[s.who]}
                        </span>
                      </div>
                      <div className="mt-0.5 max-w-[68ch] text-[13px] text-ink-2">{s.text}</div>
                      {isCurrent && (
                        <div className="mt-3">
                          <DealSteps detail={detail.data} />
                          <DealExecution detail={detail.data} />
                        </div>
                      )}
                    </div>
                  </li>
                )
              })}
            </ol>
          </Panel>
          {ended && (
            <Panel title="Posle zatvaranja">
              <DealExecution detail={detail.data} />
            </Panel>
          )}
          <DealDisputes dealId={d.id} organizationId={activeId} />
        </div>
        <div className="flex flex-col gap-4">
          <Panel title="Uslovi posla">
            <dl className="grid grid-cols-[1fr_auto] gap-x-4 gap-y-2 text-[13.5px]">
              <dt className="text-ink-2">Nominalni iznos (bruto)</dt>
              <dd className="text-right font-medium tabular-nums">{formatMoney(d.faceValue)}</dd>
              <dt className="text-ink-2">Kupoprodajna cena</dt>
              <dd className="text-right font-medium tabular-nums">
                {formatMoney(d.purchasePrice)}
              </dd>
              <dt className="text-ink-2">Regres</dt>
              <dd className="text-right font-medium">
                {d.recourseType === 'Recourse' ? 'Sa regresom' : 'Bez regresa'}
              </dd>
              <dt className="text-ink-2">Rok isplate</dt>
              <dd className="text-right font-medium">
                {d.fundingDeadlineBusinessDays} radna dana od potpisa
              </dd>
              {myRole === 'Factor' && (
                <>
                  <dt className="text-ink-2">Naknada platforme (vi plaćate)</dt>
                  <dd className="text-right font-medium tabular-nums">
                    {formatMoney(d.platformFee)}
                  </dd>
                </>
              )}
              <dt className="border-t border-line pt-2 font-semibold">
                {myRole === 'Seller' ? 'Dobijate' : 'Plaćate prodavcu'}
              </dt>
              <dd className="border-t border-line pt-2 text-right font-semibold tabular-nums">
                {formatMoney(myRole === 'Seller' ? d.sellerNetProceeds : d.purchasePrice)}
              </dd>
            </dl>
          </Panel>
          <Panel
            title="Istorija"
            actions={<span className="text-[12.5px] text-ink-3">Nepromenljiv zapis</span>}
            padded={false}
          >
            <ul className="text-[13px]">
              {timeline.data?.map((t) => (
                <li
                  key={`${t.toState}-${t.at}`}
                  className="grid grid-cols-[132px_minmax(0,1fr)] gap-2.5 border-b border-line px-[18px] py-2 last:border-b-0"
                >
                  <span className="text-ink-3 tabular-nums">{dateTime(t.at)}</span>
                  <span>{dealStates[t.toState] ?? t.toState}</span>
                </li>
              ))}
            </ul>
            <ErrorMessage error={timeline.error} />
          </Panel>
        </div>
      </div>
    </>
  )
}
