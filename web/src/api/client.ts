// API client of the SPA (task 004): same-origin cookie session, anti-forgery header on unsafe methods,
// ProblemDetails errors (api.md) turned into ApiError.

const basePath = '/api/v1'
const antiforgeryHeader = 'X-XSRF-TOKEN'

export interface FieldError {
  code: string
  message: string
}

export class ApiError extends Error {
  readonly status: number
  readonly errorCode: string | undefined
  readonly errors: FieldError[]

  constructor(
    status: number,
    errorCode: string | undefined,
    message: string,
    errors: FieldError[] = [],
  ) {
    super(message)
    this.name = 'ApiError'
    this.status = status
    this.errorCode = errorCode
    this.errors = errors
  }
}

let antiforgeryToken: string | null = null

/** The token is bound to the signed-in user, so it is dropped whenever the sign-in state changes. */
export function resetAntiforgery() {
  antiforgeryToken = null
}

async function getAntiforgeryToken(): Promise<string> {
  if (antiforgeryToken === null) {
    const response = await fetch(`${basePath}/auth/antiforgery`, { credentials: 'same-origin' })
    if (!response.ok) {
      throw await toError(response)
    }
    antiforgeryToken = ((await response.json()) as { token: string }).token
  }
  return antiforgeryToken
}

async function toError(response: Response): Promise<ApiError> {
  let problem: { detail?: string; title?: string; errorCode?: string; errors?: FieldError[] } = {}
  try {
    problem = await response.json()
  } catch {
    // Not a ProblemDetails body (e.g. 429 from the rate limiter).
  }
  return new ApiError(
    response.status,
    problem.errorCode,
    problem.detail ?? problem.title ?? response.statusText,
    Array.isArray(problem.errors) ? problem.errors : [],
  )
}

export type Method = 'GET' | 'POST' | 'PUT' | 'PATCH' | 'DELETE'

export interface RequestOptions {
  /** Optimistic concurrency (api.md §1): the version the client last read. */
  ifMatch?: number
}

let activeOrganizationId: string | null = null

/** Sent as X-Organization-Id on every request (api.md §1); route-scoped endpoints take the organization from the URL. */
export function setActiveOrganization(id: string | null) {
  activeOrganizationId = id
}

export async function api<T = void>(
  method: Method,
  path: string,
  body?: unknown,
  options: RequestOptions = {},
  idempotencyKey: string = crypto.randomUUID(),
  retried = false,
): Promise<T> {
  const headers: Record<string, string> = { Accept: 'application/json' }
  const isForm = body instanceof FormData
  if (body !== undefined && !isForm) {
    headers['Content-Type'] = 'application/json'
  }
  if (activeOrganizationId) {
    headers['X-Organization-Id'] = activeOrganizationId
  }
  if (method !== 'GET') {
    headers[antiforgeryHeader] = await getAntiforgeryToken()
    // One key per command; a retry of the same call (renewed anti-forgery token) reuses it.
    headers['Idempotency-Key'] = idempotencyKey
  }
  if (options.ifMatch !== undefined) {
    headers['If-Match'] = '"' + options.ifMatch + '"'
  }

  const response = await fetch(basePath + path, {
    method,
    headers,
    // A FormData body (file upload) sets its own multipart Content-Type.
    body: body === undefined ? undefined : isForm ? body : JSON.stringify(body),
    credentials: 'same-origin',
  })

  if (!response.ok) {
    const error = await toError(response)
    if (error.errorCode === 'csrf_failed' && !retried) {
      resetAntiforgery()
      return api<T>(method, path, body, options, idempotencyKey, true)
    }
    throw error
  }

  const contentType = response.headers.get('Content-Type') ?? ''
  return (contentType.includes('json') ? await response.json() : undefined) as T
}

/** Downloads a file with the session and organization headers (a plain link cannot send X-Organization-Id). */
export async function download(path: string, fileName: string): Promise<void> {
  const headers: Record<string, string> = {}
  if (activeOrganizationId) {
    headers['X-Organization-Id'] = activeOrganizationId
  }
  const response = await fetch(basePath + path, { headers, credentials: 'same-origin' })
  if (!response.ok) {
    throw await toError(response)
  }
  const url = URL.createObjectURL(await response.blob())
  const link = document.createElement('a')
  link.href = url
  link.download = fileName
  link.click()
  URL.revokeObjectURL(url)
}
