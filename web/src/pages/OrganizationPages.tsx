import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useEffect, useState } from 'react'
import { Link, useNavigate, useParams, useSearchParams } from 'react-router'
import { organizationsApi, type Member, type Organization } from '../api/organizations'
import { useStepUp } from '../auth/stepUpContext'
import { Button, Card, ErrorMessage, Field } from '../components/ui'
import { useActiveOrganization } from '../organizations/activeOrganization'
import { KybSection } from './KybPages'
import { MfaResetApprovals, PristupnicaSection, SellerChecklist } from './OnboardingPages'
import { SefConnectionSection } from './ReceivablePages'

const statusLabels: Record<string, string> = {
  Draft: 'Nacrt',
  Submitted: 'Poslato na proveru',
  UnderReview: 'U proveri',
  Approved: 'Odobreno',
  Rejected: 'Odbijeno',
  Suspended: 'Suspendovano',
  ClaimClosed: 'Zatvoreno (firmu je registrovao drugi nalog ili je rok istekao)',
}

/** Organization switcher: the active organization is sent with every API call. */
export function OrganizationSwitcher() {
  const { activeId, setActiveId } = useActiveOrganization()
  const mine = useQuery({ queryKey: ['organizations'], queryFn: organizationsApi.mine })

  useEffect(() => {
    if (mine.data && mine.data.length > 0 && !mine.data.some((o) => o.id === activeId)) {
      setActiveId(mine.data[0].id)
    }
  }, [mine.data, activeId, setActiveId])

  if (!mine.data || mine.data.length === 0) {
    return null
  }

  return (
    <label className="flex items-center gap-2 text-sm">
      Organizacija
      <select
        className="h-9 rounded-md border border-slate-300 bg-white px-2"
        value={activeId ?? ''}
        onChange={(e) => setActiveId(e.target.value)}
      >
        {mine.data.map((o) => (
          <option key={o.id} value={o.id}>
            {o.legalName ?? `PIB ${o.pib}`} ({o.type === 'Seller' ? 'prodavac' : 'faktor'})
          </option>
        ))}
      </select>
    </label>
  )
}

/** The start-seller wizard, step 1: the PIB creates a private Draft claim (ADR-020). */
export function StartSellerPage() {
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const { setActiveId } = useActiveOrganization()
  const [pib, setPib] = useState('')
  const create = useMutation({
    mutationFn: () => organizationsApi.createSeller(pib.trim()),
    onSuccess: async (organization) => {
      setActiveId(organization.id)
      await queryClient.invalidateQueries({ queryKey: ['organizations'] })
      navigate(`/organizations/${organization.id}`)
    },
  })

  return (
    <Card title="Nova firma (prodavac)">
      <p className="mb-4 text-sm text-slate-600">
        Korak 1 od 3: unesite PIB firme. Firma ostaje vaš privatni nacrt dok je ne potvrdite SEF
        ključem.
      </p>
      <form className="space-y-4" onSubmit={(e) => (e.preventDefault(), create.mutate())}>
        <Field
          id="pib"
          label="PIB"
          inputMode="numeric"
          maxLength={9}
          required
          value={pib}
          onChange={(e) => setPib(e.target.value)}
        />
        <ErrorMessage error={create.error} />
        <Button type="submit" className="w-full" disabled={create.isPending}>
          Nastavi
        </Button>
      </form>
    </Card>
  )
}

function DetailsForm({ organization }: { organization: Organization }) {
  const queryClient = useQueryClient()
  const [form, setForm] = useState({
    legalName: organization.legalName ?? '',
    mb: organization.mb ?? '',
    legalForm: organization.legalForm ?? '',
    registeredAddress: organization.registeredAddress ?? '',
  })
  const save = useMutation({
    mutationFn: () =>
      organizationsApi.update(
        organization.id,
        { ...form, mb: form.mb.trim() === '' ? undefined : form.mb.trim() },
        organization.version,
      ),
    onSuccess: (updated) => queryClient.setQueryData(['organization', organization.id], updated),
  })
  const editable = organization.status === 'Draft'

  return (
    <form className="space-y-3" onSubmit={(e) => (e.preventDefault(), save.mutate())}>
      <Field
        id="legalName"
        label="Poslovno ime"
        disabled={!editable}
        value={form.legalName}
        onChange={(e) => setForm({ ...form, legalName: e.target.value })}
      />
      <Field
        id="mb"
        label="Matični broj"
        disabled={!editable}
        inputMode="numeric"
        maxLength={8}
        value={form.mb}
        onChange={(e) => setForm({ ...form, mb: e.target.value })}
      />
      <Field
        id="legalForm"
        label="Pravna forma"
        disabled={!editable}
        value={form.legalForm}
        onChange={(e) => setForm({ ...form, legalForm: e.target.value })}
      />
      <Field
        id="registeredAddress"
        label="Sedište"
        disabled={!editable}
        value={form.registeredAddress}
        onChange={(e) => setForm({ ...form, registeredAddress: e.target.value })}
      />
      <ErrorMessage error={save.error} />
      {editable && (
        <Button type="submit" variant="secondary" disabled={save.isPending}>
          Sačuvaj podatke
        </Button>
      )}
    </form>
  )
}

function Representatives({ organization }: { organization: Organization }) {
  const queryClient = useQueryClient()
  const [name, setName] = useState('')
  const [role, setRole] = useState('')
  const representatives = useQuery({
    queryKey: ['representatives', organization.id],
    queryFn: () => organizationsApi.representatives(organization.id),
  })
  const add = useMutation({
    mutationFn: () => organizationsApi.addRepresentative(organization.id, name, role),
    onSuccess: async () => {
      setName('')
      setRole('')
      await queryClient.invalidateQueries({ queryKey: ['representatives', organization.id] })
    },
  })

  return (
    <section className="space-y-2">
      <h2 className="font-medium">Zakonski zastupnici</h2>
      <ul className="text-sm">
        {representatives.data?.map((r) => (
          <li key={r.id}>
            {r.name} · {r.role}
          </li>
        ))}
      </ul>
      {organization.status === 'Draft' && (
        <form
          className="grid grid-cols-2 gap-2"
          onSubmit={(e) => (e.preventDefault(), add.mutate())}
        >
          <Field
            id="repName"
            label="Ime i prezime"
            required
            value={name}
            onChange={(e) => setName(e.target.value)}
          />
          <Field
            id="repRole"
            label="Funkcija"
            required
            value={role}
            onChange={(e) => setRole(e.target.value)}
          />
          <Button type="submit" variant="secondary" className="col-span-2" disabled={add.isPending}>
            Dodaj zastupnika
          </Button>
        </form>
      )}
      <ErrorMessage error={representatives.error ?? add.error} />
    </section>
  )
}

export function OrganizationPage() {
  const { id = '' } = useParams()
  const queryClient = useQueryClient()
  const organization = useQuery({
    queryKey: ['organization', id],
    queryFn: () => organizationsApi.get(id),
  })
  const terms = useMutation({
    mutationFn: async () =>
      organizationsApi.acceptTerms(id, (await organizationsApi.currentTerms()).version),
  })
  const submit = useMutation({
    mutationFn: () => organizationsApi.submit(id),
    onSuccess: async (updated) => {
      queryClient.setQueryData(['organization', id], updated)
      await queryClient.invalidateQueries({ queryKey: ['organizations'] })
    },
  })

  if (!organization.data) {
    return (
      <Card title="Organizacija">
        <ErrorMessage error={organization.error} />
      </Card>
    )
  }

  const o = organization.data
  return (
    <Card title={o.legalName ?? `PIB ${o.pib}`}>
      <p className="mb-4 text-sm text-slate-600">
        {o.type === 'Seller' ? 'Prodavac' : 'Faktor'} · status: {statusLabels[o.status] ?? o.status}
      </p>
      <div className="space-y-6">
        {o.type === 'Seller' && <SellerChecklist organization={o} />}
        <DetailsForm key={o.version} organization={o} />
        <Representatives organization={o} />
        {o.type === 'Seller' && <SefConnectionSection organizationId={o.id} />}
        {o.type === 'Seller' && o.claimed && <PristupnicaSection organizationId={o.id} />}
        <KybSection organizationId={o.id} />
        {o.status === 'Draft' && (
          <section className="space-y-2">
            <h2 className="font-medium">Uslovi korišćenja</h2>
            {terms.isSuccess ? (
              <p className="text-sm">Uslovi su prihvaćeni.</p>
            ) : (
              <Button variant="secondary" onClick={() => terms.mutate()} disabled={terms.isPending}>
                Prihvatam Uslove korišćenja
              </Button>
            )}
            <ErrorMessage error={terms.error} />
          </section>
        )}
        {o.status === 'Draft' && (
          <div>
            <Button onClick={() => submit.mutate()} disabled={submit.isPending}>
              Pošalji na proveru
            </Button>
            <ErrorMessage error={submit.error} />
          </div>
        )}
        <div className="flex gap-4 text-sm">
          {o.type === 'Factor' && (
            <Link className="underline" to={`/organizations/${o.id}/members`}>
              Članovi
            </Link>
          )}
          <Link className="underline" to="/">
            Nazad
          </Link>
        </div>
      </div>
    </Card>
  )
}

function MemberRow({
  organizationId,
  member,
  representatives,
}: {
  organizationId: string
  member: Member
  representatives: { id: string; name: string }[]
}) {
  const queryClient = useQueryClient()
  const withStepUp = useStepUp()
  const [representativeId, setRepresentativeId] = useState(member.legalRepresentativeId ?? '')
  const refresh = () => queryClient.invalidateQueries({ queryKey: ['members', organizationId] })
  const signatory = useMutation({
    mutationFn: (isSignatory: boolean) =>
      withStepUp(() =>
        organizationsApi.setSignatory(
          organizationId,
          member,
          isSignatory,
          representativeId || null,
        ),
      ),
    onSuccess: refresh,
  })
  const remove = useMutation({
    mutationFn: () => withStepUp(() => organizationsApi.removeMember(organizationId, member.id)),
    onSuccess: refresh,
  })

  return (
    <li className="space-y-2 border-b border-slate-100 pb-3 text-sm">
      <div className="flex items-center justify-between">
        <span>
          Član {member.userId.slice(0, 8)} · {member.role}
          {member.isSignatory && ' · potpisnik'}
        </span>
        <Button variant="secondary" onClick={() => remove.mutate()}>
          Ukloni
        </Button>
      </div>
      <div className="flex items-center gap-2">
        <select
          aria-label="Zakonski zastupnik"
          className="h-9 rounded-md border border-slate-300 bg-white px-2"
          value={representativeId}
          onChange={(e) => setRepresentativeId(e.target.value)}
        >
          <option value="">— zastupnik —</option>
          {representatives.map((r) => (
            <option key={r.id} value={r.id}>
              {r.name}
            </option>
          ))}
        </select>
        <Button variant="secondary" onClick={() => signatory.mutate(!member.isSignatory)}>
          {member.isSignatory ? 'Ukloni potpisnika' : 'Postavi za potpisnika'}
        </Button>
      </div>
      <ErrorMessage error={signatory.error ?? remove.error} />
    </li>
  )
}

export function MembersPage() {
  const { id = '' } = useParams()
  const queryClient = useQueryClient()
  const withStepUp = useStepUp()
  const [email, setEmail] = useState('')
  const members = useQuery({
    queryKey: ['members', id],
    queryFn: () => organizationsApi.members(id),
  })
  const representatives = useQuery({
    queryKey: ['representatives', id],
    queryFn: () => organizationsApi.representatives(id),
  })
  const invite = useMutation({
    mutationFn: () => withStepUp(() => organizationsApi.invite(id, email)),
    onSuccess: async () => {
      setEmail('')
      await queryClient.invalidateQueries({ queryKey: ['members', id] })
    },
  })

  return (
    <Card title="Članovi">
      <ul className="mb-4 space-y-3">
        {members.data?.map((m) => (
          <MemberRow
            key={`${m.id}-${m.version}`}
            organizationId={id}
            member={m}
            representatives={representatives.data ?? []}
          />
        ))}
      </ul>
      <MfaResetApprovals organizationId={id} />
      <form className="space-y-3" onSubmit={(e) => (e.preventDefault(), invite.mutate())}>
        <Field
          id="inviteEmail"
          label="Pozovi člana (email)"
          type="email"
          required
          value={email}
          onChange={(e) => setEmail(e.target.value)}
        />
        <ErrorMessage error={invite.error ?? members.error} />
        {invite.isSuccess && <p className="text-sm">Poziv je poslat.</p>}
        <Button type="submit" disabled={invite.isPending}>
          Pošalji poziv
        </Button>
      </form>
      <Link className="mt-4 block text-sm underline" to={`/organizations/${id}`}>
        Nazad
      </Link>
    </Card>
  )
}

export function AcceptInvitationPage() {
  const [params] = useSearchParams()
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const { setActiveId } = useActiveOrganization()
  const [token] = useState(() => params.get('token') ?? '')
  const accept = useMutation({
    mutationFn: () => organizationsApi.acceptInvitation(token),
    onSuccess: async ({ organizationId }) => {
      setActiveId(organizationId)
      await queryClient.invalidateQueries({ queryKey: ['organizations'] })
      navigate(`/organizations/${organizationId}`)
    },
  })

  useEffect(() => window.history.replaceState(null, '', window.location.pathname), [])

  return (
    <Card title="Poziv u organizaciju">
      <Button className="w-full" onClick={() => accept.mutate()} disabled={accept.isPending}>
        Prihvati poziv
      </Button>
      <ErrorMessage error={accept.error} />
    </Card>
  )
}
