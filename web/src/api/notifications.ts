import { api } from './client'

export interface NotificationItem {
  id: string
  type: string
  link: string
  reference: string | null
  organizationId: string
  createdAt: string
  readAt: string | null
}

export interface PreferenceRow {
  type: string
  channel: 'inapp' | 'email'
  mandatory: boolean
  enabled: boolean
}

export interface Preferences {
  language: 'sr' | 'en'
  items: PreferenceRow[]
}

export interface Money {
  amount: string
  currency: string
}

export interface FactorAppetite {
  minAmount: Money | null
  maxAmount: Money | null
  minTenorDays: number | null
  maxTenorDays: number | null
  digestEnabled: boolean
}

/** Task 019: in-app notifications, preferences and the factor appetite. */
export const notificationsApi = {
  list: (before?: string) =>
    api<{ items: NotificationItem[]; next: string | null }>(
      'GET',
      `/notifications${before ? `?before=${encodeURIComponent(before)}` : ''}`,
    ),
  unread: () => api<{ unread: number }>('GET', '/notifications/unread-count'),
  read: (id: string) => api('POST', `/notifications/${id}/read`),
  preferences: () => api<Preferences>('GET', '/notification-preferences'),
  setPreferences: (
    language: string,
    items: { type: string; channel: string; enabled: boolean }[],
  ) => api<Preferences>('PUT', '/notification-preferences', { language, items }),
  appetite: (orgId: string) =>
    api<FactorAppetite>('GET', `/organizations/${orgId}/factor-appetite`),
  setAppetite: (orgId: string, appetite: FactorAppetite) =>
    api<FactorAppetite>('PUT', `/organizations/${orgId}/factor-appetite`, appetite),
}
