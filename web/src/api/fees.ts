import { api, download } from './client'
import type { Money } from './receivables'

export interface FeeCharge {
  id: string
  kind: 'Charge' | 'Reversal'
  dealId: string
  dealReference: string
  invoiceNumber: string
  payerOrgId: string
  payerRole: 'Factor' | 'Seller'
  amount: Money
  vat: Money
  vatPending: boolean
  status: string
  dueAt: string | null
  externalInvoiceRef: string | null
  reversesId: string | null
  reason: string | null
  fundedAt: string
}

export interface Statement {
  period: string
  lines: FeeCharge[]
  totals: { net: Money; vat: Money; total: Money }[]
}

export const feesApi = {
  mine: () => api<FeeCharge[]>('GET', '/fees/charges'),
  statement: (period: string) => api<Statement>('GET', `/fees/statements/${period}`),
  adminList: (period: string) =>
    api<FeeCharge[]>('GET', `/admin/fees/charges?period=${encodeURIComponent(period)}`),
  invoiced: (id: string, externalInvoiceRef: string) =>
    api<FeeCharge>('POST', `/admin/fees/charges/${id}/invoiced`, { externalInvoiceRef }),
  paid: (id: string, paidOn: string, reference: string) =>
    api<FeeCharge>('POST', `/admin/fees/charges/${id}/paid`, { paidOn, reference }),
  reverse: (id: string, reason: string) =>
    api<FeeCharge>('POST', `/admin/fees/charges/${id}/reverse`, { reason }),
  approveReversal: (id: string) =>
    api<FeeCharge>('POST', `/admin/fees/charges/${id}/approve-reversal`),
  exportCsv: (period: string) =>
    download(`/admin/fees/export?period=${encodeURIComponent(period)}`, `cesio-fees-${period}.csv`),
}
