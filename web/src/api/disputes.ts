import { api } from './client'

export interface DisputeSummary {
  id: string
  dealId: string
  kind: string
  status: 'Open' | 'Resolved' | 'Withdrawn'
  description: string | null
  openedByOrgId: string | null
  openedAt: string
  assignedTo: string | null
  resolutionSummary: string | null
  closedAt: string | null
}

export interface DisputeComment {
  id: string
  authorOrgId: string | null
  byStaff: boolean
  text: string | null
  documentId: string | null
  internal: boolean
  createdAt: string
}

export interface DisputeView {
  dispute: DisputeSummary
  comments: DisputeComment[]
}

export const disputeKinds = [
  'FundingDiscrepancy',
  'ContractMismatch',
  'InvoiceDisputedByDebtor',
  'FraudSuspicion',
  'FeeDispute',
  'Other',
] as const

/** Task 021: disputes on deals. */
export const disputesApi = {
  forDeal: (dealId: string) => api<DisputeSummary[]>('GET', `/deals/${dealId}/disputes`),
  open: (dealId: string, kind: string, description: string) =>
    api<DisputeView>('POST', `/deals/${dealId}/disputes`, { kind, description, documentIds: [] }),
  get: (id: string) => api<DisputeView>('GET', `/disputes/${id}`),
  comment: (id: string, text: string) =>
    api('POST', `/disputes/${id}/comments`, { text, internal: false }),
  withdraw: (id: string) => api<DisputeSummary>('POST', `/disputes/${id}/withdraw`),
  queue: (status?: string) =>
    api<DisputeSummary[]>('GET', `/admin/disputes${status ? `?status=${status}` : ''}`),
  staffGet: (id: string) => api<DisputeView>('GET', `/admin/disputes/${id}`),
  staffComment: (id: string, text: string, internal: boolean) =>
    api('POST', `/admin/disputes/${id}/comments`, { text, internal }),
  assign: (id: string) => api<DisputeSummary>('POST', `/admin/disputes/${id}/assign`),
  resolve: (id: string, summary: string) =>
    api<DisputeSummary>('POST', `/admin/disputes/${id}/resolve`, { summary }),
}
