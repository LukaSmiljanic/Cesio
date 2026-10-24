import { api } from './client'

export interface BankAccount {
  id: string
  accountNumber: string
  currency: string
  bankName: string
  status: 'ConfirmationPending' | 'Pending' | 'Verified' | 'Rejected' | 'Deactivated'
  /** Staff only (null for the owner). */
  duplicateFlag: boolean | null
  verifiedAt: string | null
  usableFrom: string | null
  deactivatedAt: string | null
  rejectionReason: string | null
  version: number
}

export interface BankAccountReview {
  account: BankAccount
  pib: string
  legalName: string | null
}

const accounts = (orgId: string) => `/organizations/${orgId}/bank-accounts`

/** Task 009: payout accounts and their compliance verification. */
export const bankAccountsApi = {
  list: (orgId: string) => api<BankAccount[]>('GET', accounts(orgId)),
  add: (orgId: string, accountNumber: string) =>
    api<BankAccount>('POST', accounts(orgId), { accountNumber, currency: 'RSD' }),
  deactivate: (orgId: string, id: string) =>
    api<BankAccount>('POST', `${accounts(orgId)}/${id}/deactivate`),
  queue: (status?: string) =>
    api<BankAccountReview[]>(
      'GET',
      `/admin/bank-accounts${status ? `?status=${encodeURIComponent(status)}` : ''}`,
    ),
  uploadEvidence: (id: string, file: File) => {
    const form = new FormData()
    form.append('file', file)
    return api<{ documentId: string }>('POST', `/admin/bank-accounts/${id}/evidence`, form)
  },
  verify: (id: string, evidenceDocumentId: string, supplementDocumentId: string | null) =>
    api<BankAccount>('POST', `/admin/bank-accounts/${id}/verify`, {
      evidenceDocumentId,
      supplementDocumentId,
      holderMatchesPib: true,
    }),
  reject: (id: string, reason: string) =>
    api<BankAccount>('POST', `/admin/bank-accounts/${id}/reject`, { reason }),
  clearDuplicate: (id: string, reason: string) =>
    api<BankAccount>('POST', `/admin/bank-accounts/${id}/clear-duplicate`, { reason }),
}
