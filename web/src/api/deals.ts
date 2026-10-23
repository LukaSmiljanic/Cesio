import { api } from './client'
import type { Money } from './receivables'

export interface Deal {
  id: string
  reference: string
  state: string
  receivableId: string
  listingId: string
  offerId: string
  sellerOrgId: string
  factorOrgId: string
  faceValue: Money
  purchasePrice: Money
  recourseType: 'Recourse' | 'NonRecourse'
  fundingDeadlineBusinessDays: number
  conditions: string | null
  feeScheduleVersion: number
  platformFee: Money
  sellerFee: Money
  sellerNetProceeds: Money
  signatureDeadline: string
  fundingDeadline: string | null
  acceptedAt: string
}

export interface DealStateChange {
  fromState: string | null
  toState: string
  at: string
  reason: string | null
}

export const dealsApi = {
  accept: (offerId: string) =>
    api<{ dealId: string; reference: string }>('POST', `/offers/${offerId}/accept`, {
      acknowledgements: { netProceedsShown: true, termsVersion: null },
    }),
  list: () => api<Deal[]>('GET', '/deals'),
  get: (id: string) => api<Deal>('GET', `/deals/${id}`),
  timeline: (id: string) => api<DealStateChange[]>('GET', `/deals/${id}/timeline`),
}
