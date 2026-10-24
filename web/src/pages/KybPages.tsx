import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { Link, useParams } from 'react-router'
import { kybApi } from '../api/kyb'
import { useStepUp } from '../auth/stepUpContext'
import { Button, Card, ErrorMessage, Field } from '../components/ui'

const kybStatuses: Record<string, string> = {
  Open: 'Otvoreno',
  InReview: 'U pregledu',
  PendingSecondApproval: 'Čeka drugo odobrenje',
  Approved: 'Odobreno',
  Rejected: 'Odbijeno',
  NeedsInfo: 'Potrebni dodatni podaci',
}

/** The organization's beneficial owners and its KYB data-sharing consent (task 007). */
export function KybSection({ organizationId }: { organizationId: string }) {
  const queryClient = useQueryClient()
  const kyb = useQuery({
    queryKey: ['kyb', organizationId],
    queryFn: () => kybApi.mine(organizationId),
  })
  const [name, setName] = useState('')
  const [percent, setPercent] = useState('')
  const [nationality, setNationality] = useState('RS')
  const [pep, setPep] = useState(false)
  const refresh = () => queryClient.invalidateQueries({ queryKey: ['kyb', organizationId] })
  const declare = useMutation({
    mutationFn: () =>
      kybApi.declareOwners(organizationId, [
        ...(kyb.data?.beneficialOwners ?? []),
        {
          name: name.trim(),
          dateOfBirth: null,
          nationality: nationality.trim().toUpperCase() || null,
          ownershipPercent: percent.trim() ? Number.parseFloat(percent) : null,
          controlType: percent.trim() ? null : 'Kontrola',
          pepDeclared: pep,
        },
      ]),
    onSuccess: () => {
      setName('')
      setPercent('')
      return refresh()
    },
  })
  const consent = useMutation({
    mutationFn: (value: boolean) => kybApi.consent(organizationId, value),
    onSuccess: refresh,
  })

  return (
    <section className="mt-4 space-y-2 text-sm">
      <h2 className="font-medium">KYB</h2>
      <p>
        Status provere:{' '}
        {kyb.data?.status ? (kybStatuses[kyb.data.status] ?? kyb.data.status) : 'nije poslato'}
      </p>
      <h3 className="font-medium">Stvarni vlasnici (≥ 25% ili kontrola)</h3>
      <ul>
        {kyb.data?.beneficialOwners.map((o) => (
          <li key={o.name}>
            {o.name} · {o.ownershipPercent !== null ? `${o.ownershipPercent}%` : o.controlType} ·{' '}
            {o.nationality} {o.pepDeclared && '· funkcioner (PEP)'}
          </li>
        ))}
      </ul>
      <div className="grid grid-cols-2 gap-2">
        <Field
          id="boName"
          label="Ime i prezime"
          value={name}
          onChange={(e) => setName(e.target.value)}
        />
        <Field
          id="boPercent"
          label="Udeo u % (prazno = kontrola bez udela)"
          inputMode="decimal"
          value={percent}
          onChange={(e) => setPercent(e.target.value)}
        />
        <Field
          id="boNationality"
          label="Državljanstvo (ISO, npr. RS)"
          value={nationality}
          onChange={(e) => setNationality(e.target.value)}
        />
        <label className="flex items-center gap-2">
          <input type="checkbox" checked={pep} onChange={(e) => setPep(e.target.checked)} />
          Funkcioner (PEP)
        </label>
      </div>
      <Button disabled={declare.isPending || name.trim() === ''} onClick={() => declare.mutate()}>
        Dodaj stvarnog vlasnika
      </Button>
      <label className="flex items-center gap-2">
        <input
          type="checkbox"
          checked={kyb.data?.sharingConsent ?? false}
          disabled={consent.isPending || !kyb.data?.status}
          onChange={(e) => consent.mutate(e.target.checked)}
        />
        Saglasan sam da se KYB paket firme deli sa faktorom posla radi njegove provere (CDD)
      </label>
      <ErrorMessage error={kyb.error ?? declare.error ?? consent.error} />
    </section>
  )
}

/** The compliance KYB queue (task 007). */
export function KybQueuePage() {
  const queue = useQuery({ queryKey: ['kyb-queue'], queryFn: kybApi.queue })
  return (
    <Card title="KYB provere">
      <ul className="space-y-2 text-sm">
        {queue.data?.map((c) => (
          <li key={c.id}>
            <Link className="underline" to={`/admin/kyb/${c.id}`}>
              {c.legalName ?? c.pib}
            </Link>{' '}
            · {c.type} · {kybStatuses[c.status] ?? c.status}
            {c.riskRating && ` · rizik ${c.riskRating}`}
          </li>
        ))}
      </ul>
      {queue.data?.length === 0 && (
        <p className="text-sm text-slate-600">Nema otvorenih provera.</p>
      )}
      <ErrorMessage error={queue.error} />
    </Card>
  )
}

/** One KYB case: screening hits, risk rating and the decision (four-eyes for high risk). */
export function KybCasePage() {
  const { id = '' } = useParams()
  const queryClient = useQueryClient()
  const withStepUp = useStepUp()
  const [note, setNote] = useState('')
  const [rating, setRating] = useState('Low')
  const [verified, setVerified] = useState(false)
  const detail = useQuery({ queryKey: ['kyb-case', id], queryFn: () => kybApi.detail(id) })
  const refresh = () => queryClient.invalidateQueries({ queryKey: ['kyb-case', id] })
  const resolve = useMutation({
    mutationFn: ({ resultId, result }: { resultId: string; result: 'Clear' | 'ConfirmedMatch' }) =>
      kybApi.resolve(id, resultId, result, note.trim()),
    onSuccess: refresh,
  })
  const risk = useMutation({
    mutationFn: () => kybApi.risk(id, rating, note.trim()),
    onSuccess: refresh,
  })
  const rescreen = useMutation({ mutationFn: () => kybApi.rescreen(id), onSuccess: refresh })
  const decide = useMutation({
    mutationFn: (decision: string) =>
      withStepUp(() => kybApi.decide(id, decision, note.trim(), verified)),
    onSuccess: refresh,
  })
  const c = detail.data
  if (!c) {
    return (
      <Card title="KYB provera">
        <ErrorMessage error={detail.error} />
      </Card>
    )
  }

  return (
    <Card title={`KYB: ${c.organization?.legalName ?? ''}`}>
      <p className="text-sm">
        PIB {c.organization?.pib} · {kybStatuses[c.status] ?? c.status}
        {c.riskRating && ` · rizik ${c.riskRating} (${c.riskReason})`}
      </p>
      <h2 className="mt-2 font-medium">Zastupnici</h2>
      <ul className="text-sm">
        {c.representatives.map((r) => (
          <li key={r}>{r}</li>
        ))}
      </ul>
      <h2 className="mt-2 font-medium">Stvarni vlasnici</h2>
      <ul className="text-sm">
        {c.beneficialOwners.map((o) => (
          <li key={o.name}>
            {o.name} · {o.ownershipPercent !== null ? `${o.ownershipPercent}%` : o.controlType} ·{' '}
            {o.nationality}
            {o.pepDeclared && ' · PEP'}
          </li>
        ))}
      </ul>
      <h2 className="mt-2 font-medium">Provera sankcionih lista</h2>
      <ul className="space-y-1 text-sm">
        {c.screening.map((s) => (
          <li key={s.id} className="flex items-center justify-between gap-2">
            <span>
              {s.subjectName} ({s.subjectKind}): {s.result}
              {s.entryName && ` · ${s.listSource}: ${s.entryName} (${s.score})`}
              {s.resolutionNote && ` · ${s.resolutionNote}`}
            </span>
            {s.result === 'PotentialMatch' && (
              <span className="flex gap-1">
                <Button
                  variant="secondary"
                  onClick={() => resolve.mutate({ resultId: s.id, result: 'Clear' })}
                >
                  Nije ista osoba
                </Button>
                <Button
                  onClick={() => resolve.mutate({ resultId: s.id, result: 'ConfirmedMatch' })}
                >
                  Potvrđeno poklapanje
                </Button>
              </span>
            )}
          </li>
        ))}
      </ul>
      <Button variant="secondary" disabled={rescreen.isPending} onClick={() => rescreen.mutate()}>
        Ponovi proveru
      </Button>
      <Field
        id="kybNote"
        label="Napomena / razlog"
        value={note}
        onChange={(e) => setNote(e.target.value)}
      />
      <div className="flex items-center gap-2 text-sm">
        <select
          aria-label="Rizik"
          className="rounded border border-slate-300 px-2 py-1"
          value={rating}
          onChange={(e) => setRating(e.target.value)}
        >
          <option value="Low">Nizak</option>
          <option value="Medium">Srednji</option>
          <option value="High">Visok (dva službenika)</option>
        </select>
        <Button
          variant="secondary"
          disabled={risk.isPending || note.trim() === ''}
          onClick={() => risk.mutate()}
        >
          Postavi rizik
        </Button>
      </div>
      <label className="mt-2 flex items-center gap-2 text-sm">
        <input type="checkbox" checked={verified} onChange={(e) => setVerified(e.target.checked)} />
        Zastupnici su provereni u APR
      </label>
      <div className="mt-2 flex gap-2">
        {['Approved', 'NeedsInfo', 'Rejected'].map((d) => (
          <Button
            key={d}
            variant={d === 'Approved' ? 'primary' : 'secondary'}
            disabled={decide.isPending || note.trim() === ''}
            onClick={() => decide.mutate(d)}
          >
            {d === 'Approved' ? 'Odobri' : d === 'NeedsInfo' ? 'Traži dopunu' : 'Odbij'}
          </Button>
        ))}
      </div>
      <ErrorMessage error={resolve.error ?? risk.error ?? rescreen.error ?? decide.error} />
    </Card>
  )
}
