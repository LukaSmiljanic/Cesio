import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useState, type ChangeEvent } from 'react'
import { Link, useParams } from 'react-router'
import { download } from '../api/client'
import { dealsApi, type DealDetail } from '../api/deals'
import { formatMoney } from '../api/receivables'
import { useStepUp } from '../auth/stepUpContext'
import { Button, Card, ErrorMessage, Field } from '../components/ui'
import { DealExecution } from './DealExecution'

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

const waitingLabels: Record<string, string> = {
  Seller: 'Na potezu je prodavac.',
  Factor: 'Na potezu je faktor.',
  Both: 'Na potezu su obe strane.',
}

function dateTime(value: string): string {
  return new Date(value).toLocaleString('sr-Latn')
}

/** The deals of the active organization, as seller or factor (task 014). */
export function DealsPage() {
  const deals = useQuery({ queryKey: ['deals'], queryFn: dealsApi.list })
  return (
    <Card title="Poslovi">
      <ul className="space-y-2 text-sm">
        {deals.data?.map((d) => (
          <li key={d.id} className="border-b border-slate-100 pb-2">
            <Link className="underline" to={`/deals/${d.id}`}>
              {d.reference}
            </Link>{' '}
            · {formatMoney(d.purchasePrice)} · {dealStates[d.state] ?? d.state}
          </li>
        ))}
      </ul>
      {deals.data?.length === 0 && <p className="text-sm text-slate-600">Nema poslova.</p>}
      <ErrorMessage error={deals.error} />
    </Card>
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
    <section className="mt-4 space-y-3 text-sm">
      {steps.waitingFor && (
        <p className="rounded bg-amber-50 p-2 text-amber-900">{waitingLabels[steps.waitingFor]}</p>
      )}
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

/** A deal with its terms, the steps before signing and the timeline (tasks 014, 015). */
export function DealPage() {
  const { id = '' } = useParams()
  const detail = useQuery({ queryKey: ['deal', id], queryFn: () => dealsApi.get(id) })
  const timeline = useQuery({
    queryKey: ['deal-timeline', id],
    queryFn: () => dealsApi.timeline(id),
  })
  const d = detail.data?.deal
  if (!detail.data || !d) {
    return (
      <Card title="Posao">
        <ErrorMessage error={detail.error} />
      </Card>
    )
  }

  return (
    <Card title={`Posao ${d.reference}`}>
      <dl className="grid grid-cols-2 gap-1 text-sm">
        <dt>Status</dt>
        <dd>{dealStates[d.state] ?? d.state}</dd>
        <dt>Iznos potraživanja</dt>
        <dd>{formatMoney(d.faceValue)}</dd>
        <dt>Kupoprodajna cena</dt>
        <dd>{formatMoney(d.purchasePrice)}</dd>
        <dt>Prodavac prima neto</dt>
        <dd>{formatMoney(d.sellerNetProceeds)}</dd>
        <dt>Naknada platforme</dt>
        <dd>{formatMoney(d.platformFee)}</dd>
        <dt>Regres</dt>
        <dd>{d.recourseType === 'Recourse' ? 'Sa regresom' : 'Bez regresa'}</dd>
        <dt>Rok za potpise</dt>
        <dd>{dateTime(d.signatureDeadline)}</dd>
      </dl>
      <DealSteps detail={detail.data} />
      <DealExecution detail={detail.data} />
      <h2 className="mt-4 font-medium">Tok posla</h2>
      <ol className="text-sm">
        {timeline.data?.map((t) => (
          <li key={`${t.toState}-${t.at}`}>
            {dateTime(t.at)} · {dealStates[t.toState] ?? t.toState}
          </li>
        ))}
      </ol>
      <ErrorMessage error={timeline.error} />
      <Link to="/deals" className="mt-4 block text-sm underline">
        Nazad
      </Link>
    </Card>
  )
}
