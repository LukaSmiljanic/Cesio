import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { api, ApiError, resetAntiforgery } from './client'

function json(status: number, body: unknown) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' },
  })
}

beforeEach(() => resetAntiforgery())
afterEach(() => vi.unstubAllGlobals())

describe('api', () => {
  it('sends the anti-forgery token on unsafe methods only', async () => {
    const fetchMock = vi.fn(async (input: RequestInfo | URL) =>
      String(input).endsWith('/auth/antiforgery')
        ? json(200, { token: 'token-1' })
        : new Response(null, { status: 204 }),
    )
    vi.stubGlobal('fetch', fetchMock)

    await api('GET', '/me')
    await api('POST', '/auth/logout')

    const [, getInit] = fetchMock.mock.calls[0] as unknown as [string, RequestInit]
    const [, postInit] = fetchMock.mock.calls[2] as unknown as [string, RequestInit]
    expect((getInit.headers as Record<string, string>)['X-XSRF-TOKEN']).toBeUndefined()
    expect((postInit.headers as Record<string, string>)['X-XSRF-TOKEN']).toBe('token-1')
  })

  it('turns ProblemDetails into an ApiError with the error code', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(async () =>
        json(401, { status: 401, detail: 'Confirm your code.', errorCode: 'step_up_required' }),
      ),
    )

    const error = await api('GET', '/me').catch((e: unknown) => e)

    expect(error).toBeInstanceOf(ApiError)
    expect((error as ApiError).status).toBe(401)
    expect((error as ApiError).errorCode).toBe('step_up_required')
  })

  it('renews the anti-forgery token once when it was rejected', async () => {
    let tokens = 0
    let posts = 0
    vi.stubGlobal(
      'fetch',
      vi.fn(async (input: RequestInfo | URL) => {
        if (String(input).endsWith('/auth/antiforgery'))
          return json(200, { token: `token-${++tokens}` })
        return ++posts === 1
          ? json(400, { errorCode: 'csrf_failed' })
          : new Response(null, { status: 204 })
      }),
    )

    await api('POST', '/auth/logout')

    expect(tokens).toBe(2)
    expect(posts).toBe(2)
  })
})
