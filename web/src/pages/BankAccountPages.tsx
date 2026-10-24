import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { bankAccountsApi, type BankAccountReview } from '../api/bankAccounts'
import { useStepUp } from '../auth/stepUpContext'
import { Button, Card, ErrorMessage, Field } from '../components/ui'

const statuses: Record<string, string> = {
  ConfirmationPending: 'čeka potvrdu sa službenog emaila',
  Pending: 'čeka proveru',
  Verified: 'proveren',
  Rejected: 'odbijen',
  Deactivated: 'deaktiviran',
}

function usability(usableFrom: string | null) {
  if (!usableFrom) {
    return ''
  }
  const from = new Date(usableFrom)
  return from > new Date()
    ? ` · za isplate od ${from.toLocaleString('sr-RS')}`
    : ' · može da prima isplate'
}

/** The organization's payout accounts (task 009). */
export function BankAccountsSection({ organizationId }: { organizationId: string }) {
  const queryClient = useQueryClient()
  const withStepUp = useStepUp()
  const [number, setNumber] = useState('')
  const list = useQuery({
    queryKey: ['bank-accounts', organizationId],
    queryFn: () => bankAccountsApi.list(organizationId),
  })
  const refresh = () =>
    queryClient.invalidateQueries({ queryKey: ['bank-accounts', organizationId] })
  const add = useMutation({
    mutationFn: () => withStepUp(() => bankAccountsApi.add(organizationId, number)),
    onSuccess: () => {
      setNumber('')
      return refresh()
    },
  })
  const deactivate = useMutation({
    mutationFn: (id: string) => withStepUp(() => bankAccountsApi.deactivate(organizationId, id)),
    onSuccess: refresh,
  })

  return (
    <section className="space-y-2 text-sm">
      <h2 className="font-medium">Računi za isplatu</h2>
      <ul className="space-y-1">
        {list.data?.map((a) => (
          <li key={a.id} className="flex items-center justify-between gap-2">
            <span>
              {a.accountNumber} · {a.bankName} · {statuses[a.status] ?? a.status}
              {a.status === 'Verified' && usability(a.usableFrom)}
              {a.rejectionReason && ` · razlog: ${a.rejectionReason}`}
            </span>
            {(a.status === 'Verified' ||
              a.status === 'Pending' ||
              a.status === 'ConfirmationPending') && (
              <Button
                variant="secondary"
                onClick={() => deactivate.mutate(a.id)}
                disabled={deactivate.isPending}
              >
                Deaktiviraj
              </Button>
            )}
          </li>
        ))}
      </ul>
      <form className="space-y-2" onSubmit={(e) => (e.preventDefault(), add.mutate())}>
        <Field
          id="accountNumber"
          label="Broj računa (npr. 160-0000000012345-67)"
          required
          value={number}
          onChange={(e) => setNumber(e.target.value)}
        />
        <p className="text-slate-600">
          Račun proverava Cesio u registru računa NBS. Svaki dodatni račun posle prvog potvrđuje se
          preko službenog emaila firme iz APR-a, a isplate na njega su moguće 72 sata posle provere.
        </p>
        <ErrorMessage error={add.error ?? deactivate.error ?? list.error} />
        <Button type="submit" variant="secondary" disabled={add.isPending}>
          Dodaj račun
        </Button>
      </form>
    </section>
  )
}

function ReviewRow({ review }: { review: BankAccountReview }) {
  const queryClient = useQueryClient()
  const withStepUp = useStepUp()
  const [snapshot, setSnapshot] = useState<File | null>(null)
  const [letter, setLetter] = useState<File | null>(null)
  const [reason, setReason] = useState('')
  const a = review.account
  const refresh = () => queryClient.invalidateQueries({ queryKey: ['admin-bank-accounts'] })
  const verify = useMutation({
    mutationFn: async () => {
      const evidence = (await bankAccountsApi.uploadEvidence(a.id, snapshot!)).documentId
      const supplement = letter
        ? (await bankAccountsApi.uploadEvidence(a.id, letter)).documentId
        : null
      return withStepUp(() => bankAccountsApi.verify(a.id, evidence, supplement))
    },
    onSuccess: refresh,
  })
  const reject = useMutation({
    mutationFn: () => withStepUp(() => bankAccountsApi.reject(a.id, reason)),
    onSuccess: refresh,
  })
  const clear = useMutation({
    mutationFn: () => withStepUp(() => bankAccountsApi.clearDuplicate(a.id, reason)),
    onSuccess: refresh,
  })
  return (
    <li className="space-y-2 border-t border-slate-200 pt-3">
      <p className="font-medium">
        {review.legalName ?? '—'} · PIB {review.pib}
      </p>
      <p>
        {a.accountNumber} · {a.bankName} · {statuses[a.status] ?? a.status}
      </p>
      {a.duplicateFlag && (
        <p className="text-amber-700">
          Isti račun je prijavila i druga firma: provera je blokirana.
        </p>
      )}
      <label className="block">
        <span className="block font-medium">Snimak iz registra računa NBS (obavezno)</span>
        <input type="file" onChange={(e) => setSnapshot(e.target.files?.[0] ?? null)} />
      </label>
      <label className="block">
        <span className="block font-medium">Pismo banke (opciono)</span>
        <input type="file" onChange={(e) => setLetter(e.target.files?.[0] ?? null)} />
      </label>
      <Field
        id={`reason-${a.id}`}
        label="Razlog (odbijanje ili razrešenje duplikata)"
        value={reason}
        onChange={(e) => setReason(e.target.value)}
      />
      <ErrorMessage error={verify.error ?? reject.error ?? clear.error} />
      <div className="flex gap-2">
        <Button onClick={() => verify.mutate()} disabled={!snapshot || verify.isPending}>
          Vlasnik je firma (PIB) — potvrdi
        </Button>
        <Button variant="secondary" onClick={() => reject.mutate()} disabled={reject.isPending}>
          Odbij
        </Button>
        {a.duplicateFlag && (
          <Button variant="secondary" onClick={() => clear.mutate()} disabled={clear.isPending}>
            Razreši duplikat
          </Button>
        )}
      </div>
    </li>
  )
}

/** Compliance: payout accounts to verify. */
export function AdminBankAccountsPage() {
  const queue = useQuery({
    queryKey: ['admin-bank-accounts'],
    queryFn: () => bankAccountsApi.queue(),
  })
  return (
    <Card title="Računi za isplatu">
      <ErrorMessage error={queue.error} />
      {queue.data?.length === 0 && <p className="text-sm">Nema računa za proveru.</p>}
      <ul className="space-y-4 text-sm">
        {queue.data?.map((review) => (
          <ReviewRow key={`${review.account.id}-${review.account.version}`} review={review} />
        ))}
      </ul>
    </Card>
  )
}
