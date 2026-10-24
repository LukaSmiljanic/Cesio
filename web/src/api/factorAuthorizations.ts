import { api } from './client'

export type AuthorizationKind = 'Bank' | 'FactoringCompany'

export interface FactorAuthorization {
  id: string
  organizationId?: string
  legalName?: string | null
  pib?: string | null
  kind: AuthorizationKind
  issuingAuthority: string
  decisionNumber: string | null
  decisionDate: string | null
  evidenceDocumentId: string | null
  officialSourceUrl: string | null
  sourceSnapshotDocumentId?: string | null
  verificationMethod?: string | null
  notes?: string | null
  status: string
  firstApprover?: string | null
  reverifyBy: string | null
  reason?: string | null
  decisionReason?: string | null
  createdAt: string
}

export interface AuthorizationSubmission {
  kind: AuthorizationKind
  issuingAuthority: string
  decisionNumber: string | null
  decisionDate: string | null
  evidenceDocumentId: string | null
  officialSourceUrl: string | null
}

/** Task 008: factor authorization submitted by the factor and verified by two compliance officers. */
export const factorAuthorizationsApi = {
  mine: (orgId: string) =>
    api<FactorAuthorization[]>('GET', `/organizations/${orgId}/factor-authorizations`),
  uploadEvidence: (file: File) => {
    const form = new FormData()
    form.append('file', file)
    form.append('purpose', 'KybEvidence')
    return api<{ id: string }>('POST', '/documents', form)
  },
  submit: (orgId: string, submission: AuthorizationSubmission) =>
    api<FactorAuthorization>('POST', `/organizations/${orgId}/factor-authorizations`, submission),
  queue: (status?: string) =>
    api<FactorAuthorization[]>(
      'GET',
      `/admin/factor-authorizations${status ? `?status=${encodeURIComponent(status)}` : ''}`,
    ),
  uploadSnapshot: (id: string, file: File) => {
    const form = new FormData()
    form.append('file', file)
    return api<{ documentId: string }>('POST', `/admin/factor-authorizations/${id}/snapshot`, form)
  },
  approve: (
    id: string,
    approval: {
      sourceSnapshotDocumentId: string | null
      verificationMethod: string | null
      notes: string | null
    },
  ) => api<FactorAuthorization>('POST', `/admin/factor-authorizations/${id}/approve`, approval),
  reject: (id: string, reason: string) =>
    api<FactorAuthorization>('POST', `/admin/factor-authorizations/${id}/reject`, { reason }),
  revoke: (id: string, reason: string) =>
    api<FactorAuthorization>('POST', `/admin/factor-authorizations/${id}/revoke`, { reason }),
  createFactor: (pib: string, legalName: string, firstUserEmail: string) =>
    api<{ id: string }>('POST', '/admin/factor-organizations', { pib, legalName, firstUserEmail }),
}
