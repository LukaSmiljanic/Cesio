import { useQuery } from '@tanstack/react-query'
import { Link, useParams } from 'react-router'
import { dealsApi } from '../api/deals'
import { formatMoney } from '../api/receivables'
import { Card, ErrorMessage } from '../components/ui'

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

/** A deal skeleton with its terms and timeline; the steps after creation come with tasks 015–017. */
export function DealPage() {
  const { id = '' } = useParams()
  const deal = useQuery({ queryKey: ['deal', id], queryFn: () => dealsApi.get(id) })
  const timeline = useQuery({
    queryKey: ['deal-timeline', id],
    queryFn: () => dealsApi.timeline(id),
  })
  const d = deal.data
  if (!d) {
    return (
      <Card title="Posao">
        <ErrorMessage error={deal.error} />
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
