import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useState, type FormEvent } from 'react'
import { Link, useParams } from 'react-router'
import { offersApi, type FactorOffer, type RecourseType, type SellerOffer } from '../api/offers'
import { formatMoney } from '../api/receivables'
import { Button, Card, ErrorMessage, Field } from '../components/ui'

const offerStates: Record<string, string> = {
  Active: 'Aktivna',
  Accepted: 'Prihvaćena',
  Withdrawn: 'Povučena',
  Rejected: 'Odbijena',
  Expired: 'Istekla',
  Superseded: 'Zamenjena',
}

const recourseLabels: Record<RecourseType, string> = {
  Recourse: 'Sa regresom',
  NonRecourse: 'Bez regresa',
}

const selectClass = 'mt-1 block w-full rounded border border-slate-300 px-2 py-1'

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
      <section className="mt-4 space-y-2 text-sm">
        <h2 className="font-medium">Vaša ponuda</h2>
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
    <form className="mt-4 space-y-2" onSubmit={onPreview}>
      <h2 className="font-medium">Nova ponuda</h2>
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
      <label className="block text-sm">
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
        <div className="space-y-2 rounded border border-slate-200 p-2 text-sm">
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
  const offers = useQuery({ queryKey: ['my-offers'], queryFn: offersApi.mine })
  return (
    <Card title="Moje ponude">
      <ul className="space-y-2 text-sm">
        {offers.data?.map((o) => (
          <li key={o.id} className="border-b border-slate-100 pb-2">
            <Link className="underline" to={`/marketplace/${o.listingId}`}>
              {formatMoney(o.purchasePrice)}
            </Link>{' '}
            · {recourseLabels[o.recourseType]} · naknada {formatMoney(o.factorFee)} ·{' '}
            {offerStates[o.state] ?? o.state}
          </li>
        ))}
      </ul>
      {offers.data?.length === 0 && <p className="text-sm text-slate-600">Nema ponuda.</p>}
      <ErrorMessage error={offers.error} />
    </Card>
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
  const rows = [...(offers.data ?? [])].sort((a, b) => compare(a, b, sort))

  return (
    <Card title="Ponude za listing">
      <label className="mb-2 block text-sm">
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
      <table className="w-full text-left text-sm">
        <thead>
          <tr>
            <th>Faktor</th>
            <th>Cena</th>
            <th>Primate neto</th>
            <th>Diskont</th>
            <th>Indikativni godišnji trošak</th>
            <th>Regres</th>
            <th>Status</th>
            <th />
          </tr>
        </thead>
        <tbody>
          {rows.map((o) => (
            <tr key={o.id} className="border-t border-slate-100">
              <td>{o.factorName ?? o.factorOrgId}</td>
              <td>{formatMoney(o.purchasePrice)}</td>
              <td>{formatMoney(o.metrics.sellerNetProceeds)}</td>
              <td>
                {formatMoney(o.metrics.discount)} ({o.metrics.discountPercent.toFixed(2)}%)
              </td>
              <td>
                {o.metrics.indicativeAnnualCostPercent === null
                  ? '—'
                  : `${o.metrics.indicativeAnnualCostPercent.toFixed(2)}%`}
              </td>
              <td>{recourseLabels[o.recourseType]}</td>
              <td>{offerStates[o.state] ?? o.state}</td>
              <td>
                {o.state === 'Active' && (
                  <Button
                    variant="secondary"
                    disabled={reject.isPending}
                    onClick={() => reject.mutate(o.id)}
                  >
                    Odbij
                  </Button>
                )}
              </td>
            </tr>
          ))}
        </tbody>
      </table>
      <p className="mt-2 text-xs text-slate-500">
        Godišnji trošak je indikativan (actual/365, od očekivanog datuma isplate do dospeća) i nije
        ponuda niti savet.
      </p>
      {offers.data?.length === 0 && <p className="text-sm text-slate-600">Još nema ponuda.</p>}
      <ErrorMessage error={offers.error ?? reject.error} />
      <Link to="/listings" className="mt-4 block text-sm underline">
        Nazad
      </Link>
    </Card>
  )
}
