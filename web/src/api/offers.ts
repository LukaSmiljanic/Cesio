import { api } from './client'
import type { Money } from './receivables'

export type RecourseType = 'Recourse' | 'NonRecourse'

export type OfferState = 'Active' | 'Accepted' | 'Withdrawn' | 'Rejected' | 'Expired' | 'Superseded'

export interface SubmitOfferRequest {
  purchasePrice: Money
  recourseType: RecourseType
  fundingDeadlineBusinessDays: number
  validUntil: string
  conditions: string | null
}

/** An offer as its factor sees it. */
export interface FactorOffer {
  id: string
  listingId: string
  state: OfferState
  purchasePrice: Money
  recourseType: RecourseType
  fundingDeadlineBusinessDays: number
  validUntil: string
  conditions: string | null
  feeScheduleVersion: number
  platformFee: Money
  factorFee: Money
  vatPending: boolean
  createdAt: string
}

/** An offer in the seller's comparison; the annual cost is indicative only (LQ-04). */
export interface SellerOffer {
  id: string
  factorOrgId: string
  factorName: string | null
  state: OfferState
  purchasePrice: Money
  recourseType: RecourseType
  fundingDeadlineBusinessDays: number
  validUntil: string
  conditions: string | null
  sellerFee: Money
  metrics: {
    discount: Money
    discountPercent: number
    sellerNetProceeds: Money
    tenorDays: number
    indicativeAnnualCostPercent: number | null
  }
}

export interface FeePreview {
  discount: Money
  factorFee: Money
  platformFee: Money
  vatPending: boolean
  feeScheduleVersion: number
}

export const offersApi = {
  feePreview: (listingId: string, purchasePrice: string) =>
    api<FeePreview>(
      'GET',
      `/marketplace/listings/${listingId}/fee-preview?purchasePrice=${encodeURIComponent(purchasePrice)}`,
    ),
  submit: (listingId: string, request: SubmitOfferRequest) =>
    api<FactorOffer>('POST', `/marketplace/listings/${listingId}/offers`, request),
  mine: () => api<FactorOffer[]>('GET', '/offers'),
  withdraw: (id: string) => api<{ state: OfferState }>('POST', `/offers/${id}/withdraw`),
  forListing: (listingId: string) => api<SellerOffer[]>('GET', `/listings/${listingId}/offers`),
  reject: (id: string, reason: string | null) =>
    api<{ state: OfferState }>('POST', `/offers/${id}/reject`, { reason }),
}
