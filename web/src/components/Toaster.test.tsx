import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { act, render, screen } from '@testing-library/react'
import { MemoryRouter } from 'react-router'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { Toaster } from './Toaster'

const json = (body: unknown) =>
  new Response(JSON.stringify(body), {
    status: 200,
    headers: { 'Content-Type': 'application/json' },
  })

const offer = {
  id: 'n2',
  type: 'offer_received',
  link: '/listings/l1/offers',
  reference: 'F-2026-101',
  organizationId: 'o1',
  createdAt: new Date().toISOString(),
  readAt: null,
}

afterEach(() => vi.unstubAllGlobals())

describe('toasts', () => {
  it('shows a notification that arrives while the user is in the app, not the ones already there', async () => {
    const old = { ...offer, id: 'n1', type: 'deal_cancelled' }
    let items = [old]
    vi.stubGlobal(
      'fetch',
      vi.fn(async () => json({ items, next: null })),
    )
    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    render(
      <QueryClientProvider client={queryClient}>
        <MemoryRouter>
          <Toaster />
        </MemoryRouter>
      </QueryClientProvider>,
    )
    await act(() => queryClient.refetchQueries({ queryKey: ['notifications-latest'] }))
    expect(screen.queryByText('Posao je otkazan')).not.toBeInTheDocument()
    // Let the first answer render before the next poll brings something new.
    await act(() => new Promise((resolve) => setTimeout(resolve, 50)))

    items = [offer, old]
    await act(() => queryClient.refetchQueries({ queryKey: ['notifications-latest'] }))

    expect(await screen.findByText('Nova ponuda')).toBeInTheDocument()
    expect(screen.getByText(/F-2026-101/)).toBeInTheDocument()
  })
})
