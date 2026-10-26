import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { Link, useParams, useSearchParams } from 'react-router'
import { bankAccountsApi } from '../api/bankAccounts'
import { onboardingApi, type PristupnicaReview } from '../api/onboarding'
import type { Organization } from '../api/organizations'
import { receivablesApi } from '../api/receivables'
import { useStepUp } from '../auth/stepUpContext'
import { FileDrop, OptionCards, Stepper } from '../components/inputs'
import { Button, Card, ErrorMessage, Field } from '../components/ui'

const pristupnicaStatuses: Record<string, string> = {
  Generated: 'pripremljena, čeka potpis',
  Uploaded: 'otpremljena, čeka proveru',
  EmailConfirmed: 'potvrđena preko službenog emaila, čeka proveru',
  Accepted: 'prihvaćena',
  Rejected: 'odbijena',
}

/** The seller's onboarding steps (task 007a): PIB → SEF → data → payout account → pristupnica → review. */
export function SellerChecklist({ organization }: { organization: Organization }) {
  const accounts = useQuery({
    queryKey: ['bank-accounts', organization.id],
    queryFn: () => bankAccountsApi.list(organization.id),
  })
  const sef = useQuery({
    queryKey: ['sef-connection', organization.id],
    queryFn: () => receivablesApi.sefConnection(organization.id),
  })
  const pristupnica = useQuery({
    queryKey: ['pristupnica', organization.id],
    queryFn: () => onboardingApi.pristupnica(organization.id),
    enabled: organization.claimed,
  })
  const steps = [
    { label: 'PIB', done: true },
    { label: 'SEF ključ', done: organization.claimed },
    {
      label: 'Podaci o firmi',
      done: Boolean(organization.legalName && organization.mb && organization.registeredAddress),
    },
    {
      label: 'Račun za isplatu',
      done: Boolean(accounts.data?.some((a) => a.status === 'Verified')),
    },
    { label: 'Pristupnica', done: pristupnica.data?.status === 'Accepted' },
    { label: 'Provera firme', done: organization.status === 'Approved' },
  ]
  return (
    <section className="flex flex-col gap-3 rounded-lg border border-line bg-surface p-4 text-sm">
      <div className="flex items-center justify-between">
        <h2 className="font-semibold">Registracija firme</h2>
        <span className="text-[13px] text-ink-2">
          {steps.filter((s) => s.done).length} od {steps.length} koraka
        </span>
      </div>
      <Stepper steps={steps} />
      {sef.data?.status !== 'Connected' && !organization.claimed && (
        <p className="text-slate-600">
          Firmu registrujete tek kada važeći SEF API ključ potvrdi PIB; sam unos PIB-a ništa ne
          rezerviše.
        </p>
      )}
    </section>
  )
}

/** Download the generated pristupnica, upload the signed copy (QES or a scan confirmed by the official email). */
export function PristupnicaSection({ organizationId }: { organizationId: string }) {
  const queryClient = useQueryClient()
  const [file, setFile] = useState<File | null>(null)
  const [method, setMethod] = useState<'Qes' | 'Scanned'>('Qes')
  const pristupnica = useQuery({
    queryKey: ['pristupnica', organizationId],
    queryFn: () => onboardingApi.pristupnica(organizationId),
  })
  const download = useMutation({
    mutationFn: () => onboardingApi.downloadGenerated(pristupnica.data!.generatedDocumentId),
  })
  const upload = useMutation({
    mutationFn: () => onboardingApi.uploadSigned(organizationId, file!, method),
    onSuccess: (updated) => {
      setFile(null)
      queryClient.setQueryData(['pristupnica', organizationId], updated)
    },
  })

  const p = pristupnica.data
  return (
    <section className="space-y-2 text-sm">
      <h2 className="font-medium">Pristupnica</h2>
      <ErrorMessage error={pristupnica.error} />
      {p && (
        <>
          <p>Status: {pristupnicaStatuses[p.status] ?? p.status}</p>
          {p.notes && p.status === 'Rejected' && <p>Razlog: {p.notes}</p>}
          <Button
            variant="secondary"
            onClick={() => download.mutate()}
            disabled={download.isPending}
          >
            Preuzmi pristupnicu (PDF)
          </Button>
          <ErrorMessage error={download.error} />
          {p.status !== 'Accepted' && (
            <form
              className="flex flex-col gap-3.5"
              onSubmit={(e) => (e.preventDefault(), file && upload.mutate())}
            >
              <OptionCards
                label="Kako je pristupnica potpisana"
                value={method}
                onChange={setMethod}
                options={[
                  {
                    value: 'Qes',
                    title: 'Kvalifikovanim elektronskim potpisom',
                    description: 'Potpis zastupnika na PDF-u',
                  },
                  {
                    value: 'Scanned',
                    title: 'Svojeručno, pa skenirano',
                    description: 'Potvrda stiže na službeni email firme iz APR-a',
                  },
                ]}
              />
              <FileDrop
                label="Potpisana pristupnica"
                hint="PDF"
                accept="application/pdf"
                onFile={(picked) => setFile(picked)}
              />
              <ErrorMessage error={upload.error} />
              <Button type="submit" disabled={!file || upload.isPending}>
                Otpremi
              </Button>
            </form>
          )}
        </>
      )}
    </section>
  )
}

/** The link from the official email: `/confirmations/{token}?purpose=`. */
export function ConfirmationPage() {
  const { token = '' } = useParams()
  const [params] = useSearchParams()
  const purpose = params.get('purpose') ?? ''
  const confirm = useMutation({ mutationFn: () => onboardingApi.confirm(token, purpose) })
  const labels: Record<string, string> = {
    Pristupnica: 'potpisanu pristupnicu',
    SefKeyChange: 'promenu SEF API ključa',
    MfaReset: 'reset dvofaktorske prijave',
    PayoutAccountChange: 'promenu računa za isplatu',
  }

  return (
    <Card title="Potvrda">
      {confirm.isSuccess ? (
        <p className="text-sm">
          {confirm.data.confirmed ? 'Hvala, potvrđeno.' : 'Ništa nije čekalo na ovu potvrdu.'}
        </p>
      ) : (
        <div className="space-y-4 text-sm">
          <p>Potvrđujete {labels[purpose] ?? 'zahtev'} za vašu firmu na platformi Cesio.</p>
          <ErrorMessage error={confirm.error} />
          <Button onClick={() => confirm.mutate()} disabled={confirm.isPending}>
            Potvrđujem
          </Button>
        </div>
      )}
    </Card>
  )
}

/** After the password, without the authenticator and without recovery codes (security.md §1). */
export function MfaResetRequestPage() {
  const request = useMutation({ mutationFn: onboardingApi.requestMfaReset })
  return (
    <Card title="Reset dvofaktorske prijave">
      {request.isSuccess ? (
        <p className="text-sm">
          {request.data.approvalKind === 'Email'
            ? 'Poslali smo link za potvrdu na službeni email firme iz APR-a. Posle potvrde, službenik Cesio-a izvršava reset.'
            : 'Zahtev čeka odobrenje drugog vlasnika vaše organizacije, a zatim službenika Cesio-a.'}{' '}
          Posle reseta 72 sata ne možete da odobravate osetljive radnje.
        </p>
      ) : (
        <div className="space-y-4 text-sm">
          <p>
            Ako imate kodove za oporavak, iskoristite njih. Bez njih reset mora da potvrdi firma
            (službeni email iz APR-a ili drugi vlasnik).
          </p>
          <ErrorMessage error={request.error} />
          <Button onClick={() => request.mutate()} disabled={request.isPending}>
            Zatraži reset
          </Button>
          <Link className="block underline" to="/mfa/verify">
            Nazad
          </Link>
        </div>
      )}
    </Card>
  )
}

/** Another Owner of a factor organization approves a colleague's MFA reset. */
export function MfaResetApprovals({ organizationId }: { organizationId: string }) {
  const queryClient = useQueryClient()
  const withStepUp = useStepUp()
  const requests = useQuery({
    queryKey: ['mfa-resets', organizationId],
    queryFn: () => onboardingApi.resetsForApproval(organizationId),
  })
  const approve = useMutation({
    mutationFn: (id: string) => withStepUp(() => onboardingApi.approveReset(organizationId, id)),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['mfa-resets', organizationId] }),
  })
  if (!requests.data?.items.length) {
    return null
  }
  return (
    <section className="space-y-2 text-sm">
      <h2 className="font-medium">Zahtevi za reset dvofaktorske prijave</h2>
      {requests.data.items.map((r) => (
        <div key={r.id} className="flex items-center justify-between">
          <span>
            Korisnik {r.userId.slice(0, 8)} · {new Date(r.createdAt).toLocaleString('sr-RS')}
          </span>
          <Button
            variant="secondary"
            onClick={() => approve.mutate(r.id)}
            disabled={approve.isPending}
          >
            Odobri
          </Button>
        </div>
      ))}
      <ErrorMessage error={approve.error} />
    </section>
  )
}

function PristupnicaReviewRow({ review }: { review: PristupnicaReview }) {
  const queryClient = useQueryClient()
  const withStepUp = useStepUp()
  const p = review.pristupnica
  const [signerName, setSignerName] = useState('')
  const [representativeId, setRepresentativeId] = useState(review.representatives[0]?.id ?? '')
  const [notes, setNotes] = useState('')
  const [officialEmail, setOfficialEmail] = useState('')
  const [aprExcerpt, setAprExcerpt] = useState<File | null>(null)
  const refresh = () => queryClient.invalidateQueries({ queryKey: ['admin-pristupnice'] })
  const decide = useMutation({
    mutationFn: (decision: 'Accepted' | 'Rejected') =>
      withStepUp(() =>
        onboardingApi.decide(p.id, {
          decision,
          signerName: decision === 'Accepted' ? signerName : null,
          matchedRepresentativeId: decision === 'Accepted' ? representativeId || null : null,
          powerOfAttorneyDocumentId: null,
          notes: notes || null,
        }),
      ),
    onSuccess: refresh,
  })
  const email = useMutation({
    mutationFn: () =>
      withStepUp(() =>
        onboardingApi.recordOfficialEmail(p.organizationId, officialEmail, aprExcerpt!),
      ),
    onSuccess: refresh,
  })
  const content = useMutation({
    mutationFn: (which: 'generated' | 'signed') => onboardingApi.adminContent(p.id, which),
  })

  return (
    <li className="space-y-2 border-t border-slate-200 pt-3">
      <p className="font-medium">
        {p.legalName ?? '—'} · PIB {p.pib}
      </p>
      <p>
        {pristupnicaStatuses[p.status] ?? p.status} ·{' '}
        {p.method === 'Qes' ? 'QES (proveriti ručno)' : 'skenirana'}
      </p>
      <div className="flex gap-2">
        <Button variant="secondary" onClick={() => content.mutate('generated')}>
          Generisana
        </Button>
        <Button variant="secondary" onClick={() => content.mutate('signed')}>
          Potpisana
        </Button>
      </div>
      <ErrorMessage error={content.error} />
      {p.officialEmailMissing && (
        <form className="space-y-2" onSubmit={(e) => (e.preventDefault(), email.mutate())}>
          <p className="text-amber-700">
            Službeni email iz APR-a nije upisan (bez njega: pismo na adresu sedišta, ručno).
          </p>
          <Field
            id={`email-${p.id}`}
            label="Službeni email iz APR izvoda"
            type="email"
            required
            value={officialEmail}
            onChange={(e) => setOfficialEmail(e.target.value)}
          />
          <FileDrop
            label="APR izvod (izvor emaila)"
            hint="PDF ili slika"
            onFile={(picked) => setAprExcerpt(picked)}
          />
          <ErrorMessage error={email.error} />
          <Button type="submit" variant="secondary" disabled={!aprExcerpt || email.isPending}>
            Upiši email
          </Button>
        </form>
      )}
      <Field
        id={`signer-${p.id}`}
        label="Ime potpisnika"
        value={signerName}
        onChange={(e) => setSignerName(e.target.value)}
      />
      <label className="block">
        <span className="block font-medium">Zastupnik iz APR-a</span>
        <select
          className="h-10 w-full rounded-md border border-slate-300 bg-white px-3"
          value={representativeId}
          onChange={(e) => setRepresentativeId(e.target.value)}
        >
          {review.representatives.map((r) => (
            <option key={r.id} value={r.id}>
              {r.name}, {r.role}
            </option>
          ))}
        </select>
      </label>
      <Field
        id={`notes-${p.id}`}
        label="Napomena (obavezna kod odbijanja)"
        value={notes}
        onChange={(e) => setNotes(e.target.value)}
      />
      <ErrorMessage error={decide.error} />
      <div className="flex gap-2">
        <Button onClick={() => decide.mutate('Accepted')} disabled={decide.isPending}>
          Prihvati
        </Button>
        <Button
          variant="secondary"
          onClick={() => decide.mutate('Rejected')}
          disabled={decide.isPending}
        >
          Odbij
        </Button>
      </div>
    </li>
  )
}

/** Compliance: pristupnice waiting for a decision. */
export function AdminPristupnicePage() {
  const queue = useQuery({
    queryKey: ['admin-pristupnice'],
    queryFn: onboardingApi.adminPristupnice,
  })
  return (
    <Card title="Pristupnice">
      <ErrorMessage error={queue.error} />
      {queue.data?.items.length === 0 && <p className="text-sm">Nema pristupnica za proveru.</p>}
      <ul className="space-y-4 text-sm">
        {queue.data?.items.map((review) => (
          <PristupnicaReviewRow key={review.pristupnica.id} review={review} />
        ))}
      </ul>
    </Card>
  )
}

/** Compliance: MFA resets to execute (two officers when only the official email approved). */
export function AdminMfaResetsPage() {
  const queryClient = useQueryClient()
  const withStepUp = useStepUp()
  const queue = useQuery({ queryKey: ['admin-mfa-resets'], queryFn: onboardingApi.adminResets })
  const execute = useMutation({
    mutationFn: (id: string) => withStepUp(() => onboardingApi.executeReset(id)),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['admin-mfa-resets'] }),
  })
  const statuses: Record<string, string> = {
    Requested: 'čeka potvrdu firme',
    Approved: 'potvrđeno',
    FirstExecution: 'čeka drugog službenika',
  }
  return (
    <Card title="Reset dvofaktorske prijave">
      <ErrorMessage error={queue.error ?? execute.error} />
      {queue.data?.items.length === 0 && <p className="text-sm">Nema zahteva.</p>}
      <ul className="space-y-2 text-sm">
        {queue.data?.items.map((r) => (
          <li key={r.id} className="flex items-center justify-between">
            <span>
              {r.approvalKind === 'Email' ? 'službeni email' : 'drugi vlasnik'} ·{' '}
              {statuses[r.status] ?? r.status}
            </span>
            {r.status !== 'Requested' && (
              <Button onClick={() => execute.mutate(r.id)} disabled={execute.isPending}>
                Izvrši
              </Button>
            )}
          </li>
        ))}
      </ul>
    </Card>
  )
}
