import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useEffect, useState, type ReactNode } from 'react'
import { Link, NavLink, Navigate, useNavigate } from 'react-router'
import { authApi, type Me } from '../api/auth'
import { notificationsApi } from '../api/notifications'
import { organizationsApi } from '../api/organizations'
import { useActiveOrganization } from '../organizations/activeOrganization'
import { InShell } from './styles'
import { BrandMark } from './ui'

interface NavItem {
  to: string
  label: string
  /** Staff items: the platform roles that may open them; the API checks again. */
  roles?: string[]
}

const compliance = ['ComplianceOfficer']

/** The menu of each kind of user (ux-design.md §3). */
function menu(
  me: Me,
  type: 'Seller' | 'Factor' | null,
  orgId: string | null,
): { title?: string; items: NavItem[] }[] {
  if (me.platformRoles.length > 0) {
    const sections: { title?: string; items: NavItem[] }[] = [
      {
        items: [
          {
            to: '/admin',
            label: 'Pregled',
            roles: ['PlatformAdmin', 'ComplianceOfficer', 'Operations', 'Finance'],
          },
        ],
      },
      {
        title: 'Redovi čekanja',
        items: [
          { to: '/admin/kyb', label: 'KYB provere', roles: compliance },
          { to: '/admin/factor-authorizations', label: 'Ovlašćenja faktora', roles: compliance },
          { to: '/admin/review', label: 'Potraživanja', roles: compliance },
          { to: '/admin/bank-accounts', label: 'Računi za isplatu', roles: compliance },
          { to: '/admin/pristupnice', label: 'Pristupnice', roles: compliance },
          { to: '/admin/mfa-resets', label: 'MFA resetovanja', roles: compliance },
          { to: '/admin/disputes', label: 'Sporovi', roles: ['ComplianceOfficer', 'Operations'] },
        ],
      },
      {
        title: 'Platforma',
        items: [
          { to: '/admin/fees', label: 'Naknade', roles: ['Finance'] },
          { to: '/admin/parameters', label: 'Parametri', roles: ['PlatformAdmin', 'Finance'] },
          { to: '/admin/audit', label: 'Audit', roles: ['ComplianceOfficer', 'Auditor'] },
          {
            to: '/admin/jobs',
            label: 'Pozadinski poslovi',
            roles: ['Operations', 'PlatformAdmin'],
          },
        ],
      },
    ]
    return sections
      .map((s) => ({
        ...s,
        items: s.items.filter((i) => !i.roles || i.roles.some((r) => me.platformRoles.includes(r))),
      }))
      .filter((s) => s.items.length > 0)
  }
  const organization = orgId ? [{ to: `/organizations/${orgId}`, label: 'Organizacija' }] : []
  if (type === 'Seller') {
    return [
      {
        items: [
          { to: '/dashboard/seller', label: 'Pregled' },
          { to: '/receivables', label: 'Potraživanja' },
          { to: '/listings', label: 'Oglasi i ponude' },
          { to: '/deals', label: 'Poslovi' },
          { to: '/notifications', label: 'Obaveštenja' },
          ...organization,
        ],
      },
    ]
  }
  if (type === 'Factor') {
    return [
      {
        items: [
          { to: '/dashboard/factor', label: 'Pregled' },
          { to: '/marketplace', label: 'Tržište' },
          { to: '/offers', label: 'Moje ponude' },
          { to: '/deals', label: 'Poslovi' },
          { to: '/fees', label: 'Naknade' },
          { to: '/notifications', label: 'Obaveštenja' },
          ...organization,
        ],
      },
    ]
  }
  return [
    {
      items: [
        { to: '/organizations/new', label: 'Registruj firmu' },
        { to: '/notifications', label: 'Obaveštenja' },
      ],
    },
  ]
}

function useActiveType() {
  const { activeId, setActiveId } = useActiveOrganization()
  const mine = useQuery({ queryKey: ['organizations'], queryFn: organizationsApi.mine })
  useEffect(() => {
    if (mine.data && mine.data.length > 0 && !mine.data.some((o) => o.id === activeId)) {
      setActiveId(mine.data[0].id)
    }
  }, [mine.data, activeId, setActiveId])
  const active = mine.data?.find((o) => o.id === activeId) ?? null
  return { mine: mine.data ?? [], loaded: !mine.isPending, active, setActiveId }
}

/** Where "/" leads: the dashboard of the user's role. */
export function HomeRedirect({ me }: { me: Me }) {
  const { active, loaded } = useActiveType()
  if (me.platformRoles.length > 0) {
    return (
      <Navigate
        to={
          me.platformRoles.includes('Auditor') && me.platformRoles.length === 1
            ? '/admin/audit'
            : '/admin'
        }
        replace
      />
    )
  }
  if (!loaded) {
    return null
  }
  if (!active) {
    return <Navigate to="/account" replace />
  }
  return (
    <Navigate to={active.type === 'Seller' ? '/dashboard/seller' : '/dashboard/factor'} replace />
  )
}

function useTheme() {
  const [theme, setTheme] = useState<string | null>(() => {
    try {
      return localStorage.getItem('cesio.theme')
    } catch {
      return null
    }
  })
  useEffect(() => {
    if (theme) {
      document.documentElement.dataset.theme = theme
    } else {
      delete document.documentElement.dataset.theme
    }
    try {
      if (theme) {
        localStorage.setItem('cesio.theme', theme)
      } else {
        localStorage.removeItem('cesio.theme')
      }
    } catch {
      // Storage may be unavailable; the choice then lasts for this page only.
    }
  }, [theme])
  const dark =
    theme === 'dark' ||
    (theme === null && window.matchMedia?.('(prefers-color-scheme: dark)').matches)
  return { dark, toggle: () => setTheme(dark ? 'light' : 'dark') }
}

const navLink = ({ isActive }: { isActive: boolean }) =>
  `flex items-center justify-between rounded-md px-2.5 py-2 font-medium whitespace-nowrap ${
    isActive ? 'bg-accent-soft text-ink' : 'text-ink-2 hover:bg-surface-2 hover:text-ink'
  }`

/** The ledger-style shell of design/prototype.html: fixed sidebar, top bar, work area. */
export function AppShell({ me, children }: { me: Me; children: ReactNode }) {
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const { mine, active, loaded, setActiveId } = useActiveType()
  const theme = useTheme()
  const unread = useQuery({
    queryKey: ['notifications-unread'],
    queryFn: notificationsApi.unread,
    refetchInterval: 60_000,
    enabled: me.platformRoles.length === 0,
  })
  const logout = useMutation({
    mutationFn: authApi.logout,
    onSuccess: () => {
      queryClient.clear()
      navigate('/login')
    },
  })
  // No menu until the organizations are known, so a customer never sees the "no organization" menu flash by.
  const sections =
    me.platformRoles.length > 0 || loaded ? menu(me, active?.type ?? null, active?.id ?? null) : []
  const staff = me.platformRoles.length > 0

  return (
    <div className="grid min-h-screen grid-cols-1 lg:grid-cols-[236px_minmax(0,1fr)]">
      <aside className="flex items-center gap-4 overflow-x-auto border-b border-line bg-surface px-4 py-2.5 lg:flex-col lg:items-stretch lg:gap-[22px] lg:overflow-visible lg:border-r lg:border-b-0 lg:px-3.5 lg:py-5">
        <Link
          to="/"
          className="flex items-center gap-2.5 px-2 font-display text-[19px] font-bold tracking-tight"
        >
          <BrandMark />
          <span className="hidden lg:inline">Cesio</span>
        </Link>
        <nav aria-label="Glavni meni" className="flex gap-4 lg:flex-col lg:gap-[18px]">
          {sections.map((section, index) => (
            <div key={section.title ?? index} className="flex gap-0.5 lg:flex-col">
              {section.title && (
                <div className="hidden px-2.5 pb-1 text-[11px] font-semibold tracking-[0.08em] text-ink-3 uppercase lg:block">
                  {section.title}
                </div>
              )}
              {section.items.map((item) => (
                <NavLink key={item.to} to={item.to} className={navLink} end={item.to === '/admin'}>
                  {item.label}
                  {item.to === '/notifications' && (unread.data?.unread ?? 0) > 0 && (
                    <span className="ml-2 rounded-full bg-warn-soft px-[7px] text-[11.5px] font-semibold text-warn">
                      {unread.data?.unread}
                    </span>
                  )}
                </NavLink>
              ))}
            </div>
          ))}
        </nav>
        <div className="mt-auto hidden px-2 text-xs text-ink-3 lg:block">
          {staff
            ? 'Platforma Cesio · osoblje'
            : 'Platforma ne kupuje potraživanja i ne prima sredstva.'}
        </div>
      </aside>

      <div className="flex min-w-0 flex-col">
        <header className="flex flex-wrap items-center justify-between gap-3 border-b border-line bg-surface px-4 py-3 lg:px-7">
          <div className="flex items-center gap-3">
            {!staff && mine.length > 0 && (
              <label className="flex items-center gap-2 text-[13px] text-ink-2">
                <span className="sr-only">Organizacija</span>
                <select
                  className="h-9 max-w-[280px] rounded-lg border border-line bg-surface-2 px-2.5 font-medium text-ink"
                  value={active?.id ?? ''}
                  onChange={(e) => {
                    setActiveId(e.target.value)
                    const next = mine.find((o) => o.id === e.target.value)
                    navigate(next?.type === 'Factor' ? '/dashboard/factor' : '/dashboard/seller')
                  }}
                >
                  {mine.map((o) => (
                    <option key={o.id} value={o.id}>
                      {o.legalName ?? `PIB ${o.pib}`} ·{' '}
                      {o.type === 'Seller' ? 'prodavac' : 'faktor'}
                    </option>
                  ))}
                </select>
              </label>
            )}
            {staff && (
              <span className="text-[13px] font-medium text-ink-2">
                {me.platformRoles.join(' · ')}
              </span>
            )}
          </div>
          <div className="flex items-center gap-2.5 text-[13px] text-ink-2">
            <button
              type="button"
              onClick={theme.toggle}
              className="h-8 rounded-md border border-line px-2.5 hover:bg-surface-2"
              aria-label={theme.dark ? 'Svetla tema' : 'Tamna tema'}
            >
              {theme.dark ? 'Svetla tema' : 'Tamna tema'}
            </button>
            <Link
              to="/account"
              className="flex items-center gap-2 hover:text-ink"
              title="Moj nalog"
            >
              <span className="grid size-[30px] place-items-center rounded-full border border-line bg-surface-2 text-xs font-semibold text-ink uppercase">
                {me.email.slice(0, 2)}
              </span>
              <span className="hidden max-w-[220px] truncate sm:inline">{me.email}</span>
            </Link>
            <button
              type="button"
              onClick={() => logout.mutate()}
              className="h-8 rounded-md border border-line px-2.5 hover:bg-surface-2"
            >
              Odjava
            </button>
          </div>
        </header>
        <main className="flex w-full max-w-[1240px] flex-col gap-6 px-4 py-5 lg:px-7 lg:py-7">
          <InShell.Provider value={true}>{children}</InShell.Provider>
        </main>
      </div>
    </div>
  )
}
