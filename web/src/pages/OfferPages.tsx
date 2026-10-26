import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useState, type FormEvent } from 'react'
import { Link, useNavigate, useParams } from 'react-router'
import { dealsApi } from '../api/deals'
import { useStepUp } from '../auth/stepUpContext'
import { offersApi, type FactorOffer, type RecourseType, type SellerOffer } from '../api/offers'
import { formatMoney } from '../api/receivables'
import {
  Button,
  EmptyRow,
  ErrorMessage,
  Field,
  PageHeader,
  Panel,
  StateChip,
} from '../components/ui'

const recourseLabels: Record<RecourseType, string> = {
  Recourse: 'Sa regresom',
  NonRecourse: 'Bez regresa',
}

const selectClass =
  'h-9 w-full rounded-[7px] border border-line bg-surface px-2.5 text-sm font-normal text-ink'

/** The factor's offer form with the fee preview and a confirmation step (task 013). */
export function OfferForm({
  listingId,
  ownOffer,
}: {
  listingId: string
  ownOffer: FactorOffer | null
}) {
  const queryClient = useQueryClient()
  const [price, setPrice] = useState('')
  const [recourse, setRecourse] = useState<RecourseType>('NonRecourse')
  const [fundingDays, setFundingDays] = useState('2')
  const [validUntil, setValidUntil] = useState('')
  const [conditions, setConditions] = useState('')
  const preview = useMutation({ mutationFn: () => offersApi.feePreview(listingId, price.trim()) })
  const refresh = () =>
    queryClient.invalidateQueries({ queryKey: ['marketplace-listing', listingId] })
  const submit = useMutation({
    mutationFn: () =>
      offersApi.submit(listingId, {
        purchasePrice: { amount: price.trim(), currency: 'RSD' },
        recourseType: recourse,
        fundingDeadlineBusinessDays: Number.parseInt(fundingDays, 10),
        validUntil: new Date(validUntil).toISOString(),
        conditions: conditions.trim() || null,
      }),
    onSuccess: refresh,
  })
  const withdraw = useMutation({ mutationFn: offersApi.withdraw, onSuccess: refresh })

  if (ownOffer) {
    return (
      <section className="space-y-3 text-sm">
        <p>
          {formatMoney(ownOffer.purchasePrice)} · {recourseLabels[ownOffer.recourseType]} · naknada
          platforme {formatMoney(ownOffer.factorFee)} · važi do{' '}
          {new Date(ownOffer.validUntil).toLocaleString('sr-Latn')}
        </p>
        <Button
          variant="secondary"
          disabled={withdraw.isPending}
          onClick={() => withdraw.mutate(ownOffer.id)}
        >
          Povuci ponudu
        </Button>
        <ErrorMessage error={withdraw.error} />
      </section>
    )
  }

  function onPreview(event: FormEvent) {
    event.preventDefault()
    preview.mutate()
  }

  return (
    <form className="flex flex-col gap-3" onSubmit={onPreview}>
      <Field
        id="price"
        label="Kupoprodajna cena (RSD)"
        inputMode="decimal"
        required
        value={price}
        onChange={(e) => {
          setPrice(e.target.value)
          preview.reset()
        }}
      />
      <label className="flex flex-col gap-1.5 text-[12.5px] font-semibold text-ink-2">
        Regres
        <select
          className={selectClass}
          value={recourse}
          onChange={(e) => setRecourse(e.target.value as RecourseType)}
        >
          <option value="NonRecourse">Bez regresa</option>
          <option value="Recourse">Sa regresom</option>
        </select>
      </label>
      <Field
        id="fundingDays"
        label="Rok isplate (radnih dana posle potpisa, 1–30)"
        inputMode="numeric"
        required
        value={fundingDays}
        onChange={(e) => setFundingDays(e.target.value)}
      />
      <Field
        id="validUntil"
        label="Ponuda važi do"
        type="datetime-local"
        required
        value={validUntil}
        onChange={(e) => setValidUntil(e.target.value)}
      />
      <Field
        id="conditions"
        label="Uslovi (opciono, do 1000 znakova)"
        maxLength={1000}
        value={conditions}
        onChange={(e) => setConditions(e.target.value)}
      />
      <Button type="submit" variant="secondary" disabled={preview.isPending}>
        Prikaži naknadu
      </Button>
      {preview.data && (
        <div className="space-y-2 rounded-md bg-surface-2 p-3 text-[13px]">
          <p>
            Diskont {formatMoney(preview.data.discount)} · naknada platforme koju plaćate{' '}
            {formatMoney(preview.data.factorFee)}
            {preview.data.vatPending && ' (PDV se određuje naknadno)'}
          </p>
          <p>Ponuda je obavezujuća i ne može da se menja; možete je samo povući.</p>
          <Button type="button" disabled={submit.isPending} onClick={() => submit.mutate()}>
            Potvrdi i pošalji ponudu
          </Button>
        </div>
      )}
      <ErrorMessage error={preview.error ?? submit.error} />
    </form>
  )
}

/** The factor's own offers (task 013). */
export function MyOffersPage() {
  const navigate = useNavigate()
  const offers = useQuery({ queryKey: ['my-offers'], queryFn: offersApi.mine })
  return (
    <>
      <PageHeader eyebrow="Faktor" title="Moje ponude" />
      <ErrorMessage error={offers.error} />
      <Panel padded={false}>
        <table>
          <thead>
            <tr>
              <th className="text-right">Kupoprodajna cena</th>
              <th>Regres</th>
              <th className="text-right">Naknada platforme</th>
              <th>Važi do</th>
              <th>Status</th>
            </tr>
          </thead>
          <tbody>
            {offers.data?.map((o) => (
              <tr
                key={o.id}
                className="cursor-pointer hover:bg-surface-2"
                onClick={() => navigate(`/marketplace/${o.listingId}`)}
              >
                <td className="text-right font-medium">{formatMoney(o.purchasePrice)}</td>
                <td>{recourseLabels[o.recourseType]}</td>
                <td className="text-right">{formatMoney(o.factorFee)}</td>
                <td>{new Date(o.validUntil).toLocaleString('sr-Latn')}</td>
                <td>
                  <StateChip state={o.state} />
                </td>
              </tr>
            ))}
            {offers.data?.length === 0 && (
              <EmptyRow columns={5}>Još nema ponuda. Otvorite tržište i dajte prvu.</EmptyRow>
            )}
          </tbody>
        </table>
      </Panel>
    </>
  )
}

type SortKey = 'net' | 'cost' | 'recourse'

function compare(a: SellerOffer, b: SellerOffer, key: SortKey): number {
  if (key === 'recourse') {
    return a.recourseType.localeCompare(b.recourseType)
  }
  if (key === 'cost') {
    return (
      (a.metrics.indicativeAnnualCostPercent ?? Infinity) -
      (b.metrics.indicativeAnnualCostPercent ?? Infinity)
    )
  }
  // Decimal strings with two decimals: compare as numbers of minor units, without a float.
  const minor = (o: SellerOffer) => BigInt(o.metrics.sellerNetProceeds.amount.replace('.', ''))
  const difference = minor(b) - minor(a)
  return difference > 0n ? 1 : difference < 0n ? -1 : 0
}

const percent = (value: number) => `${value.toFixed(2).replace('.', ',')} %`

/** The seller's comparison of a listing's offers (task 013); the cost is indicative (LQ-04). */
export function ListingOffersPage() {
  const { id = '' } = useParams()
  const queryClient = useQueryClient()
  const [sort, setSort] = useState<SortKey>('net')
  const offers = useQuery({
    queryKey: ['listing-offers', id],
    queryFn: () => offersApi.forListing(id),
  })
  const reject = useMutation({
    mutationFn: (offerId: string) => offersApi.reject(offerId, null),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['listing-offers', id] }),
  })
  const navigate = useNavigate()
  const withStepUp = useStepUp()
  const [confirming, setConfirming] = useState<SellerOffer | null>(null)
  const accept = useMutation({
    mutationFn: (offerId: string) => withStepUp(() => dealsApi.accept(offerId)),
    onSuccess: (deal) => navigate(`/deals/${deal.dealId}`),
  })
  const rows = [...(offers.data ?? [])].sort((a, b) => compare(a, b, sort))
  const best = [...rows].filter((o) => o.state === 'Active').sort((a, b) => compare(a, b, 'net'))[0]

  return (
    <>
      <div className="flex gap-1.5 text-[12.5px] text-ink-3">
        <Link to="/listings" className="text-ink-2 underline underline-offset-2">
          Oglasi i ponude
        </Link>
        <span>/</span>
        <span>Ponude</span>
      </div>
      <PageHeader
        eyebrow="Prodavac"
        title="Poređenje ponuda"
        actions={
          <label className="flex items-center gap-2 text-[13px] text-ink-2">
            Sortiraj po
            <select
              className={selectClass}
              value={sort}
              onChange={(e) => setSort(e.target.value as SortKey)}
            >
              <option value="net">Neto iznosu koji primate</option>
              <option value="cost">Indikativnom godišnjem trošku</option>
              <option value="recourse">Regresu</option>
            </select>
          </label>
        }
      />
      <ErrorMessage error={offers.error ?? reject.error ?? accept.error} />
      <Panel padded={false}>
        <table>
          <thead>
            <tr>
              <th>Faktor</th>
              <th className="text-right">Kupoprodajna cena</th>
              <th className="text-right">Diskont</th>
              <th className="text-right">Neto za vas</th>
              <th className="text-right">Godišnji trošak (okvirno)</th>
              <th>Uslovi</th>
              <th />
            </tr>
          </thead>
          <tbody>
            {rows.map((o) => (
              <tr key={o.id} className={o === best ? 'bg-accent-soft/55' : undefined}>
                <td className="font-medium">
                  {o.factorName ?? o.factorOrgId}
                  {o === best && (
                    <span className="block text-[11px] font-semibold whitespace-nowrap text-accent">
                      najbolja neto
                    </span>
                  )}
                </td>
                <td className="text-right">{formatMoney(o.purchasePrice)}</td>
                <td className="text-right">
                  {formatMoney(o.metrics.discount)}
                  <div className="text-xs text-ink-3">{percent(o.metrics.discountPercent)}</div>
                </td>
                <td className="text-right font-semibold">
                  {formatMoney(o.metrics.sellerNetProceeds)}
                </td>
                <td className="text-right">
                  {o.metrics.indicativeAnnualCostPercent === null
                    ? '—'
                    : percent(o.metrics.indicativeAnnualCostPercent)}
                </td>
                <td>
                  {recourseLabels[o.recourseType]}
                  <div className="text-xs whitespace-nowrap text-ink-3">
                    isplata {o.fundingDeadlineBusinessDays} radna dana
                  </div>
                </td>
                <td>
                  {o.state === 'Active' ? (
                    <span className="flex flex-col items-stretch gap-1.5">
                      <Button size="sm" onClick={() => setConfirming(o)}>
                        Prihvati
                      </Button>
                      <Button
                        size="sm"
                        variant="secondary"
                        disabled={reject.isPending}
                        onClick={() => reject.mutate(o.id)}
                      >
                        Odbij
                      </Button>
                    </span>
                  ) : (
                    <StateChip state={o.state} />
                  )}
                </td>
              </tr>
            ))}
            {offers.data?.length === 0 && <EmptyRow columns={7}>Još nema ponuda.</EmptyRow>}
          </tbody>
        </table>
      </Panel>
      <p className="rounded-md bg-surface-2 px-3 py-2.5 text-[12.5px] text-ink-2">
        Godišnji trošak je indikativan (actual/365, od očekivanog datuma isplate do dospeća) i nije
        ponuda niti savet. Naknadu platforme plaća faktor; ona ne umanjuje iznos koji primate.
      </p>
      {confirming && (
        <div
          className="fixed inset-0 z-50 grid place-items-center bg-[rgb(10_18_24/0.5)] p-4"
          role="dialog"
          aria-modal="true"
          aria-labelledby="accept-title"
        >
          <div className="max-h-[calc(100%-32px)] w-full max-w-[560px] overflow-auto rounded-xl border border-line bg-surface shadow-panel">
            <div className="px-[22px] pt-[18px] pb-1.5">
              <h2 id="accept-title" className="font-display text-xl font-bold">
                Prihvatanje ponude
              </h2>
            </div>
            <div className="flex flex-col gap-3.5 px-[22px] py-2.5">
              <dl className="grid grid-cols-[1fr_auto] gap-x-4 gap-y-2 text-[13.5px]">
                <dt className="text-ink-2">Faktor</dt>
                <dd className="text-right font-medium">
                  {confirming.factorName ?? confirming.factorOrgId}
                </dd>
                <dt className="text-ink-2">Kupoprodajna cena</dt>
                <dd className="text-right font-medium">{formatMoney(confirming.purchasePrice)}</dd>
                <dt className="text-ink-2">Regres</dt>
                <dd className="text-right font-medium">
                  {recourseLabels[confirming.recourseType]}
                </dd>
                <dt className="text-ink-2">Isplata</dt>
                <dd className="text-right font-medium">
                  {confirming.fundingDeadlineBusinessDays} radna dana posle potpisa
                </dd>
                <dt className="border-t border-line pt-2 font-semibold">Dobijate</dt>
                <dd className="border-t border-line pt-2 text-right font-semibold">
                  {formatMoney(confirming.metrics.sellerNetProceeds)}
                </dd>
              </dl>
              {confirming.conditions && (
                <p className="text-[13px] text-ink-2">Uslovi: {confirming.conditions}</p>
              )}
              <p className="rounded-md border border-warn/40 bg-warn-soft px-3 py-2.5 text-[13px]">
                Ostale ponude se zatvaraju, a potraživanje se rezerviše za ovaj posao. Potvrda traži
                kod iz aplikacije.
              </p>
            </div>
            <div className="flex flex-wrap justify-end gap-2.5 px-[22px] pt-4 pb-5">
              <Button variant="secondary" onClick={() => setConfirming(null)}>
                Odustani
              </Button>
              <Button disabled={accept.isPending} onClick={() => accept.mutate(confirming.id)}>
                Prihvati ponudu i kreiraj posao
              </Button>
            </div>
          </div>
        </div>
      )}
    </>
  )
}
