import { api } from './client'

export interface BeneficialOwner {
  name: string
  dateOfBirth: string | null
  nationality: string | null
  ownershipPercent: number | null
  controlType: string | null
  pepDeclared: boolean
}

export interface KybView {
  status: string | null
  sharingConsent: boolean
  beneficialOwners: BeneficialOwner[]
}

export interface KybCaseRow {
  id: string
  organizationId: string
  legalName: string | null
  pib: string | null
  type: string | null
  status: string
  riskRating: string | null
  createdAt: string
}

export interface ScreeningRow {
  id: string
  subjectKind: string
  subjectName: string
  listSource: string | null
  entryName: string | null
  score: number
  result: string
  resolutionNote: string | null
}

export interface KybCaseDetail {
  id: string
  status: string
  riskRating: string | null
  riskReason: string | null
  sharingConsent: boolean
  organization: {
    id: string
    legalName: string | null
    pib: string
    type: string
    status: string
  } | null
  representatives: string[]
  beneficialOwners: BeneficialOwner[]
  screening: ScreeningRow[]
}

export const kybApi = {
  mine: (orgId: string) => api<KybView>('GET', `/organizations/${orgId}/kyb`),
  declareOwners: (orgId: string, owners: BeneficialOwner[]) =>
    api<KybView>('POST', `/organizations/${orgId}/kyb/beneficial-owners`, { owners }),
  consent: (orgId: string, consent: boolean) =>
    api<{ sharingConsent: boolean }>('POST', `/organizations/${orgId}/kyb/sharing-consent`, {
      consent,
    }),
  queue: () => api<KybCaseRow[]>('GET', '/admin/kyb-cases'),
  detail: (id: string) => api<KybCaseDetail>('GET', `/admin/kyb-cases/${id}`),
  rescreen: (id: string) => api<KybCaseDetail>('POST', `/admin/kyb-cases/${id}/screening`),
  resolve: (id: string, resultId: string, result: 'Clear' | 'ConfirmedMatch', note: string) =>
    api('POST', `/admin/kyb-cases/${id}/screening/${resultId}/resolution`, { result, note }),
  risk: (id: string, rating: string, reason: string) =>
    api('POST', `/admin/kyb-cases/${id}/risk`, { rating, reason }),
  decide: (id: string, decision: string, reason: string, representativesVerified: boolean) =>
    api<{ status: string }>('POST', `/admin/kyb-cases/${id}/decision`, {
      decision,
      reason,
      representativesVerified,
    }),
}
