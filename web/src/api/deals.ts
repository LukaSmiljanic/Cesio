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
  agreedContractSha256: string | null
}

export interface DealSteps {
  cdd: { approved: boolean; reason: string | null; decidedAt: string } | null
  contract: {
    contractDocumentId: string
    documentId: string
    sha256: string
    uploadedAt: string
  } | null
  sellerConfirmed: boolean
  factorConfirmed: boolean
  waitingFor: 'Seller' | 'Factor' | 'Both' | null
}

export interface DealDetail {
  deal: Deal
  steps: DealSteps
  myRole: 'Seller' | 'Factor'
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
  get: (id: string) => api<DealDetail>('GET', `/deals/${id}`),
  timeline: (id: string) => api<DealStateChange[]>('GET', `/deals/${id}/timeline`),
  cddDecision: (id: string, decision: 'Approved' | 'Declined', reason: string | null) =>
    api<{ state: string }>('POST', `/deals/${id}/cdd-decision`, { decision, reason }),
  uploadContract: (file: File) => {
    const form = new FormData()
    form.append('file', file)
    form.append('purpose', 'Contract')
    return api<{ id: string }>('POST', '/documents', form)
  },
  attachContract: (id: string, documentId: string) =>
    api<{ contractDocumentId: string }>('POST', `/deals/${id}/contract`, { documentId }),
  confirmTerms: (id: string, contractDocumentId: string) =>
    api<{ state: string }>('POST', `/deals/${id}/terms-confirmation`, {
      contractDocumentId,
      confirmed: true,
    }),
  cancel: (id: string, reasonCode: string, comment: string | null) =>
    api<{ state: string }>('POST', `/deals/${id}/cancel`, { reasonCode, comment }),
}
