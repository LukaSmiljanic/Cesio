import { api } from './client'

export type OrganizationType = 'Seller' | 'Factor'
export type OnboardingStatus =
  'Draft' | 'Submitted' | 'UnderReview' | 'Approved' | 'Rejected' | 'Suspended' | 'ClaimClosed'

export interface MyOrganization {
  id: string
  type: OrganizationType
  status: OnboardingStatus
  pib: string
  legalName: string | null
  isSignatory: boolean
}

export interface Organization {
  id: string
  type: OrganizationType
  status: OnboardingStatus
  pib: string
  mb: string | null
  legalName: string | null
  legalForm: string | null
  registeredAddress: string | null
  claimed: boolean
  version: number
}

export interface Member {
  id: string
  userId: string
  role: string
  isSignatory: boolean
  legalRepresentativeId: string | null
  version: number
}

export interface LegalRepresentative {
  id: string
  name: string
  role: string
  version: number
}

export interface OrganizationUpdate {
  legalName?: string
  mb?: string
  legalForm?: string
  registeredAddress?: string
}

const org = (id: string) => `/organizations/${id}`

export const organizationsApi = {
  mine: () => api<MyOrganization[]>('GET', '/me/organizations'),
  createSeller: (pib: string) => api<Organization>('POST', '/organizations', { pib }),
  get: (id: string) => api<Organization>('GET', org(id)),
  update: (id: string, update: OrganizationUpdate, version: number) =>
    api<Organization>('PATCH', org(id), update, { ifMatch: version }),
  submit: (id: string) => api<Organization>('POST', `${org(id)}/submit`),
  members: (id: string) => api<Member[]>('GET', `${org(id)}/members`),
  invite: (id: string, email: string) => api('POST', `${org(id)}/members`, { email }),
  setSignatory: (
    id: string,
    member: Member,
    isSignatory: boolean,
    legalRepresentativeId: string | null,
  ) =>
    api(
      'PATCH',
      `${org(id)}/members/${member.id}`,
      { isSignatory, legalRepresentativeId },
      { ifMatch: member.version },
    ),
  removeMember: (id: string, memberId: string) => api('DELETE', `${org(id)}/members/${memberId}`),
  representatives: (id: string) =>
    api<LegalRepresentative[]>('GET', `${org(id)}/legal-representatives`),
  addRepresentative: (id: string, name: string, role: string) =>
    api('POST', `${org(id)}/legal-representatives`, { name, role }),
  currentTerms: () => api<{ version: string }>('GET', '/terms/current'),
  acceptTerms: (id: string, termsVersion: string) =>
    api('POST', `${org(id)}/terms-acceptances`, { termsVersion }),
  acceptInvitation: (token: string) =>
    api<{ organizationId: string }>('POST', '/invitations/accept', { token }),
}
