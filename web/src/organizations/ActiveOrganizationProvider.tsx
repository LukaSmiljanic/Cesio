import { useState, type ReactNode } from 'react'
import {
  ActiveOrganizationContext,
  loadActiveOrganization,
  saveActiveOrganization,
} from './activeOrganization'

export function ActiveOrganizationProvider({ children }: { children: ReactNode }) {
  const [activeId, setActive] = useState<string | null>(() => {
    const stored = loadActiveOrganization()
    saveActiveOrganization(stored)
    return stored
  })

  function setActiveId(id: string | null) {
    saveActiveOrganization(id)
    setActive(id)
  }

  return (
    <ActiveOrganizationContext.Provider value={{ activeId, setActiveId }}>
      {children}
    </ActiveOrganizationContext.Provider>
  )
}
