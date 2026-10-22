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

  it('shows the signed-in user on the home page', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(async () => json(200, { id: '1', email: 'ana@firma.rs' })),
    )

    renderAt('/')

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
