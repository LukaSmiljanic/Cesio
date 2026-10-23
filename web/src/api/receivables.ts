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
