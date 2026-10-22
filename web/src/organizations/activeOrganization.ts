import { createContext, useContext } from 'react'
import { setActiveOrganization } from '../api/client'

const storageKey = 'cesio.activeOrganization'

export function loadActiveOrganization(): string | null {
  try {
    return localStorage.getItem(storageKey)
  } catch {
    return null
  }
}

/** Remembers the organization the user works in (per browser) and sends it with every API call. */
export function saveActiveOrganization(id: string | null) {
  setActiveOrganization(id)
  try {
    if (id) {
      localStorage.setItem(storageKey, id)
    } else {
      localStorage.removeItem(storageKey)
    }
  } catch {
    // Storage may be unavailable (private mode); the choice then lasts for this page only.
  }
}

export interface ActiveOrganization {
  activeId: string | null
  setActiveId: (id: string | null) => void
}

export const ActiveOrganizationContext = createContext<ActiveOrganization>({
  activeId: null,
  setActiveId: () => {},
})

export const useActiveOrganization = () => useContext(ActiveOrganizationContext)
