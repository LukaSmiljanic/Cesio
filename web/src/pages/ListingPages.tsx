import { useInfiniteQuery, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useState, type FormEvent } from 'react'
import { Link, useParams } from 'react-router'
import { download } from '../api/client'
import { listingsApi, type AudienceType, type FeedFilters } from '../api/listings'
import { formatMoney } from '../api/receivables'
import { Button, Card, ErrorMessage, Field } from '../components/ui'

const listingStates: Record<string, string> = {
  Open: 'Otvoren',
  Closed: 'Zatvoren',
  Withdrawn: 'Povučen',
  Expired: 'Istekao',
  Suspended: 'Suspendovan',
}

const selectClass = 'mt-1 block w-full rounded border border-slate-300 px-2 py-1'

function ids(text: string): string[] {
  return text
    .split(/[\s,]+/)
    .map((id) => id.trim())
    .filter((id) => id !== '')
}

function dateTime(value: string): string {
  return new Date(value).toLocaleString('sr-Latn')
}

/** The seller's "publish listing" form on a Verified receivable (task 012). */
export function PublishListingSection({ receivableId }: { receivableId: string }) {
  const queryClient = useQueryClient()
  const [closesAt, setClosesAt] = useState('')
  const [audience, setAudience] = useState<AudienceType>('AllVerifiedFactors')
  const [selected, setSelected] = useState('')
  const [excluded, setExcluded] = useState('')
  const [reserve, setReserve] = useState('')
  const open = useMutation({
    mutationFn: () =>
      listingsApi.open(receivableId, {
        closesAt: new Date(closesAt).toISOString(),
        audience:
          audience === 'Selected'
            ? { type: audience, factorOrgIds: ids(selected) }
            : { type: audience },
        excludedFactorOrgIds: ids(excluded),
        reservePrice: reserve.trim() ? { amount: reserve.trim(), currency: 'RSD' } : null,
      }),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['receivable', receivableId] }),
  })

  function submit(event: FormEvent) {
    event.preventDefault()
    open.mutate()
  }

  return (
    <form className="space-y-2" onSubmit={submit}>
      <h2 className="font-medium">Objavi na marketplace-u</h2>
      <Field
        id="closesAt"
        label="Ponude se primaju do"
        type="datetime-local"
        required
        value={closesAt}
        onChange={(e) => setClosesAt(e.target.value)}
      />
      <label className="block text-sm">
        Kome je vidljivo
        <select
          className={selectClass}
          value={audience}
          onChange={(e) => setAudience(e.target.value as AudienceType)}
        >
          <option value="AllVerifiedFactors">Svim proverenim faktorima</option>
          <option value="Selected">Samo izabranim faktorima</option>
        </select>
      </label>
      {audience === 'Selected' && (
        <Field
          id="selected"
          label="ID-jevi izabranih faktora (odvojeni zarezom)"
          value={selected}
          onChange={(e) => setSelected(e.target.value)}
        />
      )}
      <Field
        id="excluded"
        label="ID-jevi isključenih faktora (opciono)"
        value={excluded}
        onChange={(e) => setExcluded(e.target.value)}
      />
      <Field
        id="reserve"
        label="Minimalna cena u RSD (opciono, faktori je ne vide)"
        inputMode="decimal"
        value={reserve}
        onChange={(e) => setReserve(e.target.value)}
      />
      <Button type="submit" disabled={open.isPending}>
        Objavi
      </Button>
      {open.isSuccess && <p className="text-sm">Listing je objavljen.</p>}
      <ErrorMessage error={open.error} />
    </form>
  )
}

/** The seller's listings (task 012). */
export function SellerListingsPage() {
  const queryClient = useQueryClient()
  const listings = useQuery({ queryKey: ['listings'], queryFn: listingsApi.mine })
  const withdraw = useMutation({
    mutationFn: listingsApi.withdraw,
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['listings'] }),
  })

  return (
    <Card title="Moji listinzi">
      <ul className="space-y-2 text-sm">
        {listings.data?.map((l) => (
          <li
            key={l.id}
            className="flex items-center justify-between gap-2 border-b border-slate-100 pb-2"
          >
            <span>
              <Link className="underline" to={`/receivables/${l.receivableId}`}>
                {formatMoney(l.faceValue)}
              </Link>{' '}
              · do {dateTime(l.closesAt)} · {listingStates[l.state] ?? l.state}
              {l.reservePrice && <> · minimalna cena {formatMoney(l.reservePrice)}</>}
            </span>
            {(l.state === 'Open' || l.state === 'Suspended') && (
              <Button
                variant="secondary"
                disabled={withdraw.isPending}
                onClick={() => withdraw.mutate(l.id)}
              >
                Povuci
              </Button>
            )}
          </li>
        ))}
      </ul>
      {listings.data?.length === 0 && <p className="text-sm text-slate-600">Nema listinga.</p>}
      <ErrorMessage error={listings.error ?? withdraw.error} />
    </Card>
  )
}

/** The factor marketplace feed with filters and cursor paging (task 012). */
export function MarketplacePage() {
  const [draft, setDraft] = useState<FeedFilters>({ sort: 'closesAt' })
  const [filters, setFilters] = useState<FeedFilters>({ sort: 'closesAt' })
  const feed = useInfiniteQuery({
    queryKey: ['marketplace', filters],
    queryFn: ({ pageParam }) => listingsApi.feed(filters, pageParam),
    initialPageParam: null as string | null,
    getNextPageParam: (page) => page.nextCursor,
  })

  function submit(event: FormEvent) {
    event.preventDefault()
    setFilters(draft)
  }

  const items = feed.data?.pages.flatMap((page) => page.items) ?? []
  return (
    <Card title="Marketplace">
      <form className="mb-4 grid grid-cols-2 gap-2" onSubmit={submit}>
        <Field
          id="minAmount"
          label="Iznos od (RSD)"
          inputMode="decimal"
          value={draft.minAmount ?? ''}
          onChange={(e) => setDraft({ ...draft, minAmount: e.target.value })}
        />
        <Field
          id="maxAmount"
          label="Iznos do (RSD)"
          inputMode="decimal"
          value={draft.maxAmount ?? ''}
          onChange={(e) => setDraft({ ...draft, maxAmount: e.target.value })}
        />
        <Field
          id="minDays"
          label="Dana do dospeća, od"
          inputMode="numeric"
          value={draft.minDays ?? ''}
          onChange={(e) => setDraft({ ...draft, minDays: e.target.value })}
        />
        <Field
          id="maxDays"
          label="Dana do dospeća, do"
          inputMode="numeric"
          value={draft.maxDays ?? ''}
          onChange={(e) => setDraft({ ...draft, maxDays: e.target.value })}
        />
        <Field
          id="debtorPib"
          label="PIB dužnika"
          inputMode="numeric"
          value={draft.debtorPib ?? ''}
          onChange={(e) => setDraft({ ...draft, debtorPib: e.target.value })}
        />
        <label className="block text-sm">
          Sortiranje
          <select
            className={selectClass}
            value={draft.sort}
            onChange={(e) => setDraft({ ...draft, sort: e.target.value as FeedFilters['sort'] })}
          >
            <option value="closesAt">Po roku za ponude</option>
            <option value="amount">Po iznosu</option>
          </select>
        </label>
        <Button type="submit">Primeni</Button>
      </form>
      <ul className="space-y-2 text-sm">
        {items.map((l) => (
          <li key={l.id} className="border-b border-slate-100 pb-2">
            <Link className="underline" to={`/marketplace/${l.id}`}>
              {formatMoney(l.faceValue)}
            </Link>{' '}
            · dužnik {l.debtorPib} · {l.daysToMaturity} dana do dospeća · ponude do{' '}
            {dateTime(l.closesAt)}
          </li>
        ))}
      </ul>
      {feed.isSuccess && items.length === 0 && (
        <p className="text-sm text-slate-600">Nema listinga koji odgovaraju filterima.</p>
      )}
      {feed.hasNextPage && (
        <Button
          variant="secondary"
          disabled={feed.isFetchingNextPage}
          onClick={() => void feed.fetchNextPage()}
        >
          Još
        </Button>
      )}
      <ErrorMessage error={feed.error} />
    </Card>
  )
}

/** A listing as a factor sees it (api.md §3.4): no reserve price, no other offers. */
export function MarketplaceListingPage() {
  const { id = '' } = useParams()
  const detail = useQuery({
    queryKey: ['marketplace-listing', id],
    queryFn: () => listingsApi.detail(id),
  })
  // The server logs every download; watermarking is not built yet (task 012 implementation notes).
  const fetchDocument = useMutation({
    mutationFn: ({ id: documentId, fileName }: { id: string; fileName: string }) =>
      download(`/documents/${documentId}/content`, fileName),
  })
  const l = detail.data
  if (!l) {
    return (
      <Card title="Listing">
        <ErrorMessage error={detail.error} />
      </Card>
    )
  }

  const share = `${(Number.parseFloat(l.platformFee.share) * 100).toFixed(0)}%`
  return (
    <Card title={`Faktura ${l.invoice.number}`}>
      <dl className="grid grid-cols-2 gap-1 text-sm">
        <dt>Dužnik</dt>
        <dd>
          {l.debtor.legalName} (PIB {l.debtor.pib}
          {l.debtor.mb && `, MB ${l.debtor.mb}`})
        </dd>
        <dt>Prodavac</dt>
        <dd>
          {l.seller.legalName} ({l.seller.kybStatus})
        </dd>
        <dt>Iznos potraživanja</dt>
        <dd>{formatMoney(l.faceValue)}</dd>
        <dt>Faktura (osnovica / PDV / ukupno)</dt>
        <dd>
          {formatMoney(l.invoice.netAmount)} / {formatMoney(l.invoice.vatAmount)} /{' '}
          {formatMoney(l.invoice.grossAmount)}
        </dd>
        <dt>Izdata / dospeva</dt>
        <dd>
          {l.invoice.issueDate} / {l.invoice.dueDate}
        </dd>
        <dt>SEF status</dt>
        <dd>
          {l.sef.status} (provereno {dateTime(l.sef.checkedAt)})
        </dd>
        <dt>Ponude do</dt>
        <dd>{dateTime(l.closesAt)}</dd>
        <dt>Naknada platforme</dt>
        <dd>{share} diskonta, plaća faktor</dd>
      </dl>
      <h2 className="mt-4 font-medium">Dokumenti</h2>
      <ul className="text-sm">
        {l.documents.map((d) => (
          <li key={d.id}>
            <button
              type="button"
              className="underline"
              onClick={() => fetchDocument.mutate({ id: d.id, fileName: d.fileName })}
            >
              {d.fileName}
            </button>
          </li>
        ))}
      </ul>
      <ErrorMessage error={fetchDocument.error} />
      <Link to="/marketplace" className="mt-4 block text-sm underline">
        Nazad
      </Link>
    </Card>
  )
}
