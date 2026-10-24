import { api, download } from './client'

export type PristupnicaStatus =
  'Generated' | 'Uploaded' | 'EmailConfirmed' | 'Accepted' | 'Rejected'

export interface Pristupnica {
  id: string
  organizationId: string
  pib: string
  legalName: string | null
  status: PristupnicaStatus
  method: 'Qes' | 'ScannedConfirmedByEmail' | null
  generatedDocumentId: string
  generatedSha256: string
  signedDocumentId: string | null
  signerName: string | null
  notes: string | null
  decidedAt: string | null
  officialEmailMissing: boolean
  version: number
}

export interface PristupnicaReview {
  pristupnica: Pristupnica
  representatives: { id: string; name: string; role: string }[]
}

export interface PristupnicaDecision {
  decision: 'Accepted' | 'Rejected'
  signerName: string | null
  matchedRepresentativeId: string | null
  powerOfAttorneyDocumentId: string | null
  notes: string | null
}

export interface MfaResetRow {
  id: string
  userId: string
  organizationId?: string
  approvalKind?: 'Email' | 'Owner'
  status: string
  createdAt: string
}

const org = (id: string) => `/organizations/${id}`

/** Task 007a: pristupnica, official-email confirmations and MFA reset. */
export const onboardingApi = {
  pristupnica: (orgId: string) => api<Pristupnica>('GET', `${org(orgId)}/pristupnica`),
  downloadGenerated: (documentId: string) =>
    download(`/documents/${documentId}/content`, 'pristupnica.pdf'),
  uploadSigned: async (orgId: string, file: File, method: 'Qes' | 'Scanned') => {
    const form = new FormData()
    form.append('file', file)
    form.append('purpose', 'Pristupnica')
    const document = await api<{ id: string }>('POST', '/documents', form)
    return api<Pristupnica>('POST', `${org(orgId)}/pristupnica`, {
      documentId: document.id,
      method,
    })
  },
  confirm: (token: string, purpose: string) =>
    api<{ purpose: string; confirmed: boolean }>(
      'POST',
      `/confirmations/${encodeURIComponent(token)}`,
      { purpose },
    ),
  requestMfaReset: () =>
    api<{ id: string; approvalKind: 'Email' | 'Owner'; status: string }>(
      'POST',
      '/auth/mfa-reset-requests',
      {},
    ),
  resetsForApproval: (orgId: string) =>
    api<{ items: MfaResetRow[] }>('GET', `${org(orgId)}/mfa-reset-requests`),
  approveReset: (orgId: string, requestId: string) =>
    api('POST', `${org(orgId)}/mfa-reset-requests/${requestId}/approve`),
  adminPristupnice: () => api<{ items: PristupnicaReview[] }>('GET', '/admin/pristupnice'),
  adminContent: (id: string, which: 'generated' | 'signed') =>
    download(`/admin/pristupnice/${id}/content?which=${which}`, `pristupnica-${which}.pdf`),
  decide: (id: string, decision: PristupnicaDecision) =>
    api<{ id: string; status: string }>('POST', `/admin/pristupnice/${id}/decision`, decision),
  recordOfficialEmail: async (orgId: string, email: string, aprExcerpt: File) => {
    const form = new FormData()
    form.append('file', aprExcerpt)
    const source = await api<{ documentId: string }>(
      'POST',
      `/admin/organizations/${orgId}/evidence`,
      form,
    )
    return api<{ confirmationSent: boolean }>(
      'POST',
      `/admin/organizations/${orgId}/official-email`,
      {
        email,
        sourceDocumentId: source.documentId,
      },
    )
  },
  adminResets: () => api<{ items: MfaResetRow[] }>('GET', '/admin/mfa-resets'),
  executeReset: (id: string) =>
    api<{ id: string; status: string }>('POST', `/admin/mfa-resets/${id}/execute`),
}
