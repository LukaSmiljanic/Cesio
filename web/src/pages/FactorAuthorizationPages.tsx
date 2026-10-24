import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import {
  factorAuthorizationsApi,
  type AuthorizationKind,
  type FactorAuthorization,
} from '../api/factorAuthorizations'
import { useStepUp } from '../auth/stepUpContext'
import { Button, Card, ErrorMessage, Field } from '../components/ui'

const statuses: Record<string, string> = {
  Submitted: 'poslato, čeka proveru',
  PendingSecondApproval: 'čeka drugog službenika',
  Verified: 'provereno',
  Rejected: 'odbijeno',
  Revoked: 'opozvano',
  Expired: 'isteklo',
}

const authorities: Record<AuthorizationKind, { value: string; label: string }[]> = {
  Bank: [{ value: 'NationalBankOfSerbia', label: 'Narodna banka Srbije' }],
  FactoringCompany: [
    { value: 'MinistryOfFinance', label: 'Ministarstvo finansija' },
    { value: 'SecuritiesCommission', label: 'Komisija za hartije od vrednosti' },
  ],
}

const selectClass = 'h-10 w-full rounded-md border border-slate-300 bg-white px-3 text-sm'

/** The factor's authorization (task 008): a bank gives only its kind, a factoring company its decision. */
export function FactorAuthorizationSection({ organizationId }: { organizationId: string }) {
  const queryClient = useQueryClient()
  const [kind, setKind] = useState<AuthorizationKind>('FactoringCompany')
  const [authority, setAuthority] = useState('MinistryOfFinance')
  const [decisionNumber, setDecisionNumber] = useState('')
  const [decisionDate, setDecisionDate] = useState('')
  const [url, setUrl] = useState('')
  const [file, setFile] = useState<File | null>(null)
  const mine = useQuery({
    queryKey: ['factor-authorizations', organizationId],
    queryFn: () => factorAuthorizationsApi.mine(organizationId),
  })
  const submit = useMutation({
    mutationFn: async () => {
      const isBank = kind === 'Bank'
      const evidence =
        !isBank && file ? (await factorAuthorizationsApi.uploadEvidence(file)).id : null
      return factorAuthorizationsApi.submit(organizationId, {
        kind,
        issuingAuthority: authority,
        decisionNumber: isBank ? null : decisionNumber,
        decisionDate: isBank || !decisionDate ? null : decisionDate,
        evidenceDocumentId: evidence,
        officialSourceUrl: url || null,
      })
    },
    onSuccess: () =>
      queryClient.invalidateQueries({ queryKey: ['factor-authorizations', organizationId] }),
  })

  function changeKind(next: AuthorizationKind) {
    setKind(next)
    setAuthority(authorities[next][0].value)
  }

  return (
    <section className="space-y-2 text-sm">
      <h2 className="font-medium">Ovlašćenje za faktoring</h2>
      <ul>
        {mine.data?.map((a) => (
          <li key={a.id}>
            {a.kind === 'Bank' ? 'Banka' : `Društvo za faktoring, rešenje ${a.decisionNumber}`} ·{' '}
            {statuses[a.status] ?? a.status}
            {a.reverifyBy && ` · ponovna provera do ${a.reverifyBy}`}
            {a.reason && ` · razlog: ${a.reason}`}
          </li>
        ))}
      </ul>
      <form className="space-y-2" onSubmit={(e) => (e.preventDefault(), submit.mutate())}>
        <label className="block">
          <span className="block font-medium">Vrsta</span>
          <select
            className={selectClass}
            value={kind}
            onChange={(e) => changeKind(e.target.value as AuthorizationKind)}
          >
            <option value="FactoringCompany">Društvo za faktoring</option>
            <option value="Bank">Banka</option>
          </select>
        </label>
        <label className="block">
          <span className="block font-medium">Izdavalac</span>
          <select
            className={selectClass}
            value={authority}
            onChange={(e) => setAuthority(e.target.value)}
          >
            {authorities[kind].map((a) => (
              <option key={a.value} value={a.value}>
                {a.label}
              </option>
            ))}
          </select>
        </label>
        {kind === 'FactoringCompany' && (
          <>
            <Field
              id="decisionNumber"
              label="Broj rešenja (Fa. broj)"
              required
              value={decisionNumber}
              onChange={(e) => setDecisionNumber(e.target.value)}
            />
            <Field
              id="decisionDate"
              label="Datum rešenja"
              type="date"
              required
              value={decisionDate}
              onChange={(e) => setDecisionDate(e.target.value)}
            />
            <label className="block">
              <span className="block font-medium">Rešenje o davanju saglasnosti (PDF)</span>
              <input
                type="file"
                accept="application/pdf"
                onChange={(e) => setFile(e.target.files?.[0] ?? null)}
              />
            </label>
          </>
        )}
        <Field
          id="officialUrl"
          label="Link na zvanični registar (https)"
          value={url}
          onChange={(e) => setUrl(e.target.value)}
        />
        <ErrorMessage error={submit.error ?? mine.error} />
        <Button type="submit" variant="secondary" disabled={submit.isPending}>
          Pošalji na proveru
        </Button>
      </form>
    </section>
  )
}

function AuthorizationRow({ authorization }: { authorization: FactorAuthorization }) {
  const queryClient = useQueryClient()
  const withStepUp = useStepUp()
  const [snapshot, setSnapshot] = useState<File | null>(null)
  const [method, setMethod] = useState('OfficialRegisterLookup')
  const [notes, setNotes] = useState('')
  const [reason, setReason] = useState('')
  const refresh = () => queryClient.invalidateQueries({ queryKey: ['admin-factor-authorizations'] })
  const approve = useMutation({
    mutationFn: async () => {
      const snapshotId =
        authorization.status === 'Submitted' && snapshot
          ? (await factorAuthorizationsApi.uploadSnapshot(authorization.id, snapshot)).documentId
          : null
      return withStepUp(() =>
        factorAuthorizationsApi.approve(authorization.id, {
          sourceSnapshotDocumentId: snapshotId,
          verificationMethod: authorization.status === 'Submitted' ? method : null,
          notes: notes || null,
        }),
      )
    },
    onSuccess: refresh,
  })
  const reject = useMutation({
    mutationFn: () => withStepUp(() => factorAuthorizationsApi.reject(authorization.id, reason)),
    onSuccess: refresh,
  })
  const revoke = useMutation({
    mutationFn: () => withStepUp(() => factorAuthorizationsApi.revoke(authorization.id, reason)),
    onSuccess: refresh,
  })
  const a = authorization
  return (
    <li className="space-y-2 border-t border-slate-200 pt-3">
      <p className="font-medium">
        {a.legalName ?? '—'} · PIB {a.pib}
      </p>
      <p>
        {a.kind === 'Bank'
          ? 'Banka (proveriti spisak NBS)'
          : 'Društvo za faktoring (proveriti registar APR)'}{' '}
        · {a.issuingAuthority}{' '}
        {a.decisionNumber && `· rešenje ${a.decisionNumber} od ${a.decisionDate}`} ·{' '}
        {statuses[a.status] ?? a.status}
      </p>
      {a.officialSourceUrl && (
        <a
          className="underline"
          href={a.officialSourceUrl}
          target="_blank"
          rel="noreferrer noopener"
        >
          Zvanični registar
        </a>
      )}
      {a.status === 'Submitted' && (
        <>
          <label className="block">
            <span className="block font-medium">Snimak zvaničnog izvora</span>
            <input type="file" onChange={(e) => setSnapshot(e.target.files?.[0] ?? null)} />
          </label>
          <label className="block">
            <span className="block font-medium">Način provere</span>
            <select
              className={selectClass}
              value={method}
              onChange={(e) => setMethod(e.target.value)}
            >
              <option value="OfficialRegisterLookup">Uvid u zvanični registar</option>
              <option value="AuthorityConfirmationLetter">Potvrda nadležnog organa</option>
            </select>
          </label>
          <Field
            id={`notes-${a.id}`}
            label="Napomena"
            value={notes}
            onChange={(e) => setNotes(e.target.value)}
          />
        </>
      )}
      <Field
        id={`reason-${a.id}`}
        label="Razlog (za odbijanje ili opoziv)"
        value={reason}
        onChange={(e) => setReason(e.target.value)}
      />
      <ErrorMessage error={approve.error ?? reject.error ?? revoke.error} />
      <div className="flex gap-2">
        {(a.status === 'Submitted' || a.status === 'PendingSecondApproval') && (
          <>
            <Button onClick={() => approve.mutate()} disabled={approve.isPending}>
              {a.status === 'Submitted' ? 'Prvo odobrenje' : 'Drugo odobrenje'}
            </Button>
            <Button variant="secondary" onClick={() => reject.mutate()} disabled={reject.isPending}>
              Odbij
            </Button>
          </>
        )}
        {a.status === 'Verified' && (
          <Button variant="secondary" onClick={() => revoke.mutate()} disabled={revoke.isPending}>
            Opozovi
          </Button>
        )}
      </div>
    </li>
  )
}

/** Compliance: factor authorizations to verify, and a new factor organization by invitation. */
export function AdminFactorAuthorizationsPage() {
  const queryClient = useQueryClient()
  const withStepUp = useStepUp()
  const [status, setStatus] = useState('')
  const [pib, setPib] = useState('')
  const [legalName, setLegalName] = useState('')
  const [email, setEmail] = useState('')
  const queue = useQuery({
    queryKey: ['admin-factor-authorizations', status],
    queryFn: () => factorAuthorizationsApi.queue(status || undefined),
  })
  const create = useMutation({
    mutationFn: () => withStepUp(() => factorAuthorizationsApi.createFactor(pib, legalName, email)),
    onSuccess: () => {
      setPib('')
      setLegalName('')
      setEmail('')
      return queryClient.invalidateQueries({ queryKey: ['admin-factor-authorizations'] })
    },
  })
  return (
    <Card title="Ovlašćenja faktora">
      <label className="mb-4 block text-sm">
        <span className="block font-medium">Status</span>
        <select className={selectClass} value={status} onChange={(e) => setStatus(e.target.value)}>
          <option value="">Čekaju proveru</option>
          <option value="Verified">Provereno</option>
          <option value="Rejected">Odbijeno</option>
          <option value="Revoked">Opozvano</option>
          <option value="Expired">Isteklo</option>
        </select>
      </label>
      <ErrorMessage error={queue.error} />
      {queue.data?.length === 0 && <p className="text-sm">Nema stavki.</p>}
      <ul className="space-y-4 text-sm">
        {queue.data?.map((a) => (
          <AuthorizationRow key={`${a.id}-${a.status}`} authorization={a} />
        ))}
      </ul>
      <form
        className="mt-6 space-y-2 text-sm"
        onSubmit={(e) => (e.preventDefault(), create.mutate())}
      >
        <h2 className="font-medium">Novi faktor (pozivom)</h2>
        <Field
          id="factorPib"
          label="PIB"
          required
          value={pib}
          onChange={(e) => setPib(e.target.value)}
        />
        <Field
          id="factorName"
          label="Poslovno ime"
          required
          value={legalName}
          onChange={(e) => setLegalName(e.target.value)}
        />
        <Field
          id="factorEmail"
          label="Email prvog korisnika"
          type="email"
          required
          value={email}
          onChange={(e) => setEmail(e.target.value)}
        />
        <ErrorMessage error={create.error} />
        {create.isSuccess && <p>Poziv je poslat.</p>}
        <Button type="submit" variant="secondary" disabled={create.isPending}>
          Kreiraj i pozovi
        </Button>
      </form>
    </Card>
  )
}
