import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { AppRoutes } from './App'

function json(status: number, body: unknown) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' },
  })
}

function renderAt(path: string) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter initialEntries={[path]}>
        <AppRoutes />
      </MemoryRouter>
    </QueryClientProvider>,
  )
}

afterEach(() => vi.unstubAllGlobals())

describe('App', () => {
  it('sends a visitor without a session to the login page', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(async () => json(401, { status: 401 })),
    )

    renderAt('/')

    expect(await screen.findByRole('heading', { name: 'Prijava' })).toBeInTheDocument()
  })

  it('shows the signed-in user and their organizations on the home page', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(async (input: RequestInfo | URL) =>
        String(input).endsWith('/me/organizations')
          ? json(200, [
              {
                id: 'o1',
                type: 'Seller',
                status: 'Draft',
                pib: '101134702',
                legalName: null,
                isSignatory: false,
              },
            ])
          : json(200, { id: '1', email: 'ana@firma.rs' }),
      ),
    )

    renderAt('/')

    expect(await screen.findByRole('link', { name: 'PIB 101134702' })).toBeInTheDocument()

    expect(await screen.findByText('Prijavljeni ste kao ana@firma.rs.')).toBeInTheDocument()
  })

  it('continues to MFA enrolment after the password', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(async (input: RequestInfo | URL) => {
        const url = String(input)
        if (url.endsWith('/auth/antiforgery')) return json(200, { token: 't' })
        if (url.endsWith('/auth/login')) return json(200, { next: 'enrol' })
        if (url.endsWith('/auth/mfa/enrol/start'))
          return json(200, { sharedKey: 'ABCD', otpauthUri: 'otpauth://totp/Cesio:a?secret=ABCD' })
        return json(404, {})
      }),
    )
    renderAt('/login')

    await userEvent.type(screen.getByLabelText('Email'), 'ana@firma.rs')
    await userEvent.type(screen.getByLabelText('Lozinka'), 'correct horse battery')
    await userEvent.click(screen.getByRole('button', { name: 'Prijavi se' }))

    expect(
      await screen.findByRole('heading', { name: 'Podešavanje dvofaktorske prijave' }),
    ).toBeInTheDocument()
    expect(await screen.findByText('ABCD')).toBeInTheDocument()
  })

  it('shows the API error message on a failed login', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(async (input: RequestInfo | URL) =>
        String(input).endsWith('/auth/antiforgery')
          ? json(200, { token: 't' })
          : json(401, { status: 401, errorCode: 'invalid_credentials' }),
      ),
    )
    renderAt('/login')

    await userEvent.type(screen.getByLabelText('Email'), 'ana@firma.rs')
    await userEvent.type(screen.getByLabelText('Lozinka'), 'wrong password!!')
    await userEvent.click(screen.getByRole('button', { name: 'Prijavi se' }))

    expect(await screen.findByRole('alert')).toHaveTextContent('Pogrešan email ili lozinka')
  })
})

describe('start-seller wizard', () => {
  it('creates a Draft claim from the PIB with an idempotency key and opens it', async () => {
    const fetchMock = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = String(input)
      if (url.endsWith('/auth/antiforgery')) return json(200, { token: 't' })
      if (url.endsWith('/organizations') && init?.method === 'POST')
        return json(201, {
          id: 'o1',
          type: 'Seller',
          status: 'Draft',
          pib: '101134702',
          mb: null,
          legalName: null,
          legalForm: null,
          registeredAddress: null,
          claimed: false,
          version: 0,
        })
      if (url.endsWith('/organizations/o1'))
        return json(200, {
          id: 'o1',
          type: 'Seller',
          status: 'Draft',
          pib: '101134702',
          mb: null,
          legalName: null,
          legalForm: null,
          registeredAddress: null,
          claimed: false,
          version: 0,
        })
      if (url.endsWith('/me')) return json(200, { id: '1', email: 'ana@firma.rs' })
      return json(200, [])
    })
    vi.stubGlobal('fetch', fetchMock)
    renderAt('/organizations/new')

    await userEvent.type(await screen.findByLabelText('PIB'), '101134702')
    await userEvent.click(screen.getByRole('button', { name: 'Nastavi' }))

    expect(await screen.findByRole('heading', { name: 'PIB 101134702' })).toBeInTheDocument()
    const create = fetchMock.mock.calls.find(
      ([, init]) => init?.method === 'POST' && init.body?.toString().includes('101134702'),
    )
    expect((create?.[1]?.headers as Record<string, string>)['Idempotency-Key']).toMatch(
      /[0-9a-f-]{36}/,
    )
  })

  it('confirms an official-email link only after the click, so link scanners confirm nothing', async () => {
    const fetchMock = vi.fn(async (input: RequestInfo | URL) =>
      String(input).includes('/confirmations/')
        ? json(200, { purpose: 'Pristupnica', confirmed: true })
        : json(401, { status: 401 }),
    )
    vi.stubGlobal('fetch', fetchMock)

    renderAt('/confirmations/abc?purpose=Pristupnica')
    const button = await screen.findByRole('button', { name: 'Potvrđujem' })
    const before = fetchMock.mock.calls.filter(([url]) =>
      String(url).includes('/confirmations/'),
    ).length
    await userEvent.click(button)

    expect(before).toBe(0)
    expect(await screen.findByText('Hvala, potvrđeno.')).toBeInTheDocument()
    expect(String(fetchMock.mock.calls.at(-1)?.[0])).toContain(
      '/confirmations/abc?purpose=Pristupnica',
    )
  })
})
