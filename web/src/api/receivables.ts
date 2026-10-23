import { api } from './client'

export interface Money {
  amount: string
  currency: string
}

export interface ReceivableView {
  id: string
  state: string
  faceValue: Money
  maturityDate: string
  createdAt: string
  version: number
  invoice: {
    id: string
    sefInvoiceId: string
    number: string
    issueDate: string
    dueDate: string
    gross: Money
    sefStatus: string
  }
  debtor: { id: string; pib: string; legalName: string }
}

export interface ImportResult {
  sefInvoiceId: string
  status: 'Imported' | 'AlreadyImported' | 'NotIssuer' | 'NotFound' | 'Unsupported'
  receivableId: string | null
  reason: string | null
}

export interface SefConnection {
  status: 'Connected' | 'Invalid' | null
  validatedAt: string | null
  supplierPib: string | null
}

export type ImportRequest =
  { sefInvoiceIds: string[] } | { issueDateFrom: string; issueDateTo: string }

export const receivablesApi = {
  list: () => api<ReceivableView[]>('GET', '/receivables'),
  importFromSef: (request: ImportRequest) =>
    api<{ results: ImportResult[] }>('POST', '/receivables/import-from-sef', request),
  sefConnection: (orgId: string) =>
    api<SefConnection>('GET', `/organizations/${orgId}/sef-connection`),
  connectSef: (orgId: string, apiKey: string) =>
    api<SefConnection>('PUT', `/organizations/${orgId}/sef-connection`, { apiKey }),
}

/** "1200000.00" → "1.200.000,00 RSD" (sr-Latn grouping), without going through a float. */
export function formatMoney(money: Money): string {
  const [whole, fraction = '00'] = money.amount.split('.')
  const grouped = whole.replace(/\B(?=(\d{3})+(?!\d))/g, '.')
  return `${grouped},${fraction} ${money.currency}`
}

export interface Declaration {
  version: string
  text: string
  items: string[]
  sha256: string
}

export interface CheckRow {
  run: number
  code: string
  /** 'Review': a check the seller does not see the result of (the cross-seller duplicate check). */
  result: 'Pass' | 'Warn' | 'Fail' | 'Review'
  details: string | null
  ranAt: string
}

export interface SubmitResult {
  state: string
  reviewRequired: boolean
  reason: string | null
}

export interface QueueItem {
  id: string
  sellerOrgId: string
  maturityDate: string
  createdAt: string
}

export const verificationApi = {
  get: (id: string) => api<ReceivableView>('GET', `/receivables/${id}`),
  declaration: () => api<Declaration>('GET', '/receivables/declaration'),
  saveDeclaration: (id: string, answers: Record<string, boolean>) =>
    api<{ complete: boolean; blocking: string | null }>('PUT', `/receivables/${id}/declaration`, {
      answers,
    }),
  uploadEvidence: (file: File) => {
    const form = new FormData()
    form.append('file', file)
    form.append('purpose', 'ReceivableEvidence')
    return api<{ id: string; scanStatus: string }>('POST', '/documents', form)
  },
  attach: (id: string, documentId: string) =>
    api('POST', `/receivables/${id}/documents`, { documentId }),
  submit: (id: string) => api<SubmitResult>('POST', `/receivables/${id}/submit`),
  cancel: (id: string) => api('POST', `/receivables/${id}/cancel`),
  checks: (id: string) => api<CheckRow[]>('GET', `/receivables/${id}/checks`),
  queue: () => api<QueueItem[]>('GET', '/admin/receivables/review-queue'),
  decide: (
    id: string,
    decision: 'Verified' | 'Rejected',
    reasonCode: string | null,
    comment: string,
  ) =>
    api<{ state: string }>('POST', `/admin/receivables/${id}/decision`, {
      decision,
      reasonCode,
      comment,
    }),
}
