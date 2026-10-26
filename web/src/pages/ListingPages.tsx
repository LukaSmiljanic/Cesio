import { useInfiniteQuery, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useState, type FormEvent } from 'react'
import { Link, useNavigate, useParams } from 'react-router'
import { download } from '../api/client'
import { listingsApi, type AudienceType, type FeedFilters } from '../api/listings'
import { formatMoney } from '../api/receivables'
import { Segmented } from '../components/inputs'
import {
  Button,
  Chip,
  EmptyRow,
  ErrorMessage,
  Field,
  PageHeader,
  Panel,
  StateChip,
} from '../components/ui'
import { buttonClass, formatDate } from '../components/styles'
import { OfferForm } from './OfferPages'

const selectClass =
  'h-9 w-full rounded-[7px] border border-line bg-surface px-2.5 text-sm font-normal text-ink'

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
    <form className="flex flex-col gap-3.5" onSubmit={submit}>
      <Field
        id="closesAt"
        label="Ponude se primaju do"
        type="datetime-local"
        required
        value={closesAt}
        onChange={(e) => setClosesAt(e.target.value)}
      />
      <Segmented
        label="Kome je oglas vidljiv"
        value={audience}
        onChange={setAudience}
        options={[
          { value: 'AllVerifiedFactors', label: 'Svim proverenim faktorima' },
          { value: 'Selected', label: 'Samo izabranim' },
        ]}
      />
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
  const navigate = useNavigate()
  const listings = useQuery({ queryKey: ['listings'], queryFn: listingsApi.mine })
  const withdraw = useMutation({
    mutationFn: listingsApi.withdraw,
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['listings'] }),
  })

  return (
    <>
      <PageHeader eyebrow="Prodavac" title="Oglasi i ponude" />
      <ErrorMessage error={listings.error ?? withdraw.error} />
      <Panel padded={false}>
        <table>
          <thead>
            <tr>
              <th className="text-right">Iznos potraživanja</th>
              <th>Dospeće</th>
              <th>Ponude do</th>
              <th>Minimalna cena</th>
              <th>Status</th>
              <th />
            </tr>
          </thead>
          <tbody>
            {listings.data?.map((l) => (
              <tr
                key={l.id}
                className="cursor-pointer hover:bg-surface-2"
                onClick={() => navigate(`/listings/${l.id}/offers`)}
              >
                <td className="text-right font-medium">{formatMoney(l.faceValue)}</td>
                <td>{formatDate(l.maturityDate)}</td>
                <td>{dateTime(l.closesAt)}</td>
                <td>{l.reservePrice ? formatMoney(l.reservePrice) : '—'}</td>
                <td>
                  <StateChip state={l.state} />
                </td>
                <td className="text-right" onClick={(e) => e.stopPropagation()}>
                  <div className="flex justify-end gap-2">
                    <Link to={`/listings/${l.id}/offers`} className={buttonClass('primary', 'sm')}>
                      Ponude
                    </Link>
                    {(l.state === 'Open' || l.state === 'Suspended') && (
                      <Button
                        variant="secondary"
                        size="sm"
                        disabled={withdraw.isPending}
                        onClick={() => withdraw.mutate(l.id)}
                      >
                        Povuci
                      </Button>
                    )}
                  </div>
                </td>
              </tr>
            ))}
            {listings.data?.length === 0 && (
              <EmptyRow columns={6}>
                Još nema oglasa. Objavite verifikovano potraživanje sa njegove stranice.
              </EmptyRow>
            )}
          </tbody>
        </table>
      </Panel>
    </>
  )
}

/** The factor marketplace feed with filters and cursor paging (task 012). */
export function MarketplacePage() {
  const navigate = useNavigate()
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
    <>
      <PageHeader eyebrow="Faktor" title="Tržište" />
      <Panel title="Filteri">
        <form
          className="grid grid-cols-[repeat(auto-fit,minmax(160px,1fr))] items-end gap-3.5"
          onSubmit={submit}
        >
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
          <label className="flex flex-col gap-1.5 text-[12.5px] font-semibold text-ink-2">
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
      </Panel>
      <ErrorMessage error={feed.error} />
      <Panel padded={false}>
        <table>
          <thead>
            <tr>
              <th className="text-right">Iznos potraživanja</th>
              <th>Dužnik (PIB)</th>
              <th>Dospeće</th>
              <th className="text-right">Dana do dospeća</th>
              <th>Ponude do</th>
              <th />
            </tr>
          </thead>
          <tbody>
            {items.map((l) => (
              <tr
                key={l.id}
                className="cursor-pointer hover:bg-surface-2"
                onClick={() => navigate(`/marketplace/${l.id}`)}
              >
                <td className="text-right font-medium">{formatMoney(l.faceValue)}</td>
                <td className="font-mono text-[13px]">{l.debtorPib}</td>
                <td>{formatDate(l.maturityDate)}</td>
                <td className="text-right">{l.daysToMaturity}</td>
                <td>{dateTime(l.closesAt)}</td>
                <td className="text-right">
                  <Link
                    to={`/marketplace/${l.id}`}
                    className={buttonClass('primary', 'sm')}
                    onClick={(e) => e.stopPropagation()}
                  >
                    Otvori
                  </Link>
                </td>
              </tr>
            ))}
            {feed.isSuccess && items.length === 0 && (
              <EmptyRow columns={6}>Nema oglasa koji odgovaraju filterima.</EmptyRow>
            )}
          </tbody>
        </table>
      </Panel>
      {feed.hasNextPage && (
        <div>
          <Button
            variant="secondary"
            disabled={feed.isFetchingNextPage}
            onClick={() => void feed.fetchNextPage()}
          >
            Još
          </Button>
        </div>
      )}
    </>
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
      <>
        <PageHeader eyebrow="Tržište" title="Oglas" />
        <ErrorMessage error={detail.error} />
      </>
    )
  }

  const share = `${(Number.parseFloat(l.platformFee.share) * 100).toFixed(0)} %`
  return (
    <>
      <div className="flex gap-1.5 text-[12.5px] text-ink-3">
        <Link to="/marketplace" className="text-ink-2 underline underline-offset-2">
          Tržište
        </Link>
        <span>/</span>
        <span>Faktura {l.invoice.number}</span>
      </div>
      <PageHeader
        eyebrow={`Oglas · ponude do ${dateTime(l.closesAt)}`}
        title={`${l.debtor.legalName} · ${formatMoney(l.faceValue)}`}
      />
      <div className="grid items-start gap-6 lg:grid-cols-[minmax(0,1.6fr)_minmax(0,1fr)]">
        <div className="flex flex-col gap-4">
          <Panel
            title="Faktura i dužnik"
            actions={
              <Chip tone={l.sef.status === 'Approved' ? 'pos' : 'warn'}>SEF: {l.sef.status}</Chip>
            }
          >
            <dl className="grid grid-cols-[repeat(auto-fit,minmax(170px,1fr))] gap-x-5 gap-y-3.5">
              {(
                [
                  ['Dužnik', `${l.debtor.legalName}`],
                  ['PIB / MB dužnika', `${l.debtor.pib}${l.debtor.mb ? ` / ${l.debtor.mb}` : ''}`],
                  ['Prodavac', `${l.seller.legalName ?? '—'}`],
                  ['KYB prodavca', `${l.seller.kybStatus ?? '—'}`],
                  ['Faktura', l.invoice.number],
                  ['Izdata', formatDate(l.invoice.issueDate)],
                  ['Dospeva', formatDate(l.invoice.dueDate)],
                  ['SEF provereno', dateTime(l.sef.checkedAt)],
                  ['Osnovica', formatMoney(l.invoice.netAmount)],
                  ['PDV', formatMoney(l.invoice.vatAmount)],
                  ['Ukupno', formatMoney(l.invoice.grossAmount)],
                  ['Naknada platforme', `${share} diskonta, plaća faktor`],
                ] as [string, string][]
              ).map(([label, value]) => (
                <div key={label} className="min-w-0">
                  <dt className="mb-0.5 text-xs text-ink-3">{label}</dt>
                  <dd className="font-medium break-words">{value}</dd>
                </div>
              ))}
            </dl>
          </Panel>
          <Panel title="Dokumenti" padded={false}>
            <ul>
              {l.documents.map((d) => (
                <li
                  key={d.id}
                  className="flex items-center justify-between border-b border-line px-[18px] py-2.5 last:border-b-0"
                >
                  <span>{d.fileName}</span>
                  <Button
                    variant="secondary"
                    size="sm"
                    onClick={() => fetchDocument.mutate({ id: d.id, fileName: d.fileName })}
                  >
                    Preuzmi
                  </Button>
                </li>
              ))}
              {l.documents.length === 0 && (
                <li className="px-[18px] py-4 text-ink-2">Nema priloženih dokumenata.</li>
              )}
            </ul>
          </Panel>
          <ErrorMessage error={fetchDocument.error} />
        </div>
        <Panel title={l.ownOffer ? 'Vaša ponuda' : 'Dajte ponudu'}>
          <OfferForm listingId={l.id} ownOffer={l.ownOffer} />
        </Panel>
      </div>
    </>
  )
}
