import { api } from './client'
import type { FactorOffer } from './offers'
import type { Money } from './receivables'

export type AudienceType = 'AllVerifiedFactors' | 'Selected'

export interface OpenListingRequest {
  closesAt: string
  audience: { type: AudienceType; factorOrgIds?: string[] }
  excludedFactorOrgIds: string[]
  reservePrice: Money | null
}

export interface SellerListing {
  id: string
  receivableId: string
  state: 'Open' | 'Closed' | 'Withdrawn' | 'Expired' | 'Suspended'
  opensAt: string
  closesAt: string
  audienceType: AudienceType
  faceValue: Money
  reservePrice: Money | null
  maturityDate: string
  suspendedReason: string | null
}

export interface FeedItem {
  id: string
  faceValue: Money
  maturityDate: string
  daysToMaturity: number
  debtorPib: string
  debtorName: string | null
  sefStatus: string | null
  closesAt: string
  /** The viewer's own Active offer on this listing; competing offers are never sent (api.md §3.4). */
  ownOffer: { id: string; purchasePrice: Money } | null
}

export interface FeedFilters {
  minAmount?: string
  maxAmount?: string
  minDays?: string
  maxDays?: string
  debtorPib?: string
  sort?: 'closesAt' | 'amount'
  withoutOwnOffer?: 'true'
}

export interface ListingDetail {
  id: string
  closesAt: string
  faceValue: Money
  debtor: { legalName: string; pib: string; mb: string | null }
  invoice: {
    number: string
    issueDate: string
    dueDate: string
    netAmount: Money
    vatAmount: Money
    grossAmount: Money
  }
  sef: { status: string; checkedAt: string }
  seller: { legalName: string | null; kybStatus: string | null }
  documents: {
    id: string
    purpose: string
    fileName: string
    contentType: string
    sizeBytes: number
  }[]
  platformFee: { basis: string; share: string; payer: string }
  ownOffer: FactorOffer | null
}

function query(filters: FeedFilters, cursor: string | null): string {
  const params = new URLSearchParams()
  for (const [key, value] of Object.entries(filters)) {
    if (value) {
      params.set(key, value)
    }
  }
  if (cursor) {
    params.set('cursor', cursor)
  }
  const text = params.toString()
  return text ? `?${text}` : ''
}

export const listingsApi = {
  open: (receivableId: string, request: OpenListingRequest) =>
    api<SellerListing>('POST', `/receivables/${receivableId}/listings`, request),
  mine: () => api<SellerListing[]>('GET', '/listings'),
  withdraw: (id: string) => api<{ state: string }>('POST', `/listings/${id}/withdraw`),
  feed: (filters: FeedFilters, cursor: string | null) =>
    api<{ items: FeedItem[]; nextCursor: string | null }>(
      'GET',
      `/marketplace/listings${query(filters, cursor)}`,
    ),
  detail: (id: string) => api<ListingDetail>('GET', `/marketplace/listings/${id}`),
}
