// Shared helpers of the local demo scripts (dev-seed.mjs, dev-seed-market.mjs, dev-totp.mjs). Local stack only.
import { createHmac, randomUUID } from 'node:crypto'

export const base = process.env.CESIO_URL ?? 'http://localhost:5174'
export const mailbox = process.env.CESIO_SMTP4DEV ?? 'http://localhost:5000'
if (!['localhost', '127.0.0.1'].includes(new URL(base).hostname)) throw new Error(`Only for a local stack, not ${base}.`)

/** RFC 6238 code (HMAC-SHA1, 30 s, 6 digits) for a base32 key. */
export function totp(base32Key, stepOffset = 0) {
  const alphabet = 'ABCDEFGHIJKLMNOPQRSTUVWXYZ234567'
  let bits = ''
  for (const c of base32Key.replace(/[\s=]/g, '').toUpperCase()) bits += alphabet.indexOf(c).toString(2).padStart(5, '0')
  const key = Buffer.from(bits.match(/.{8}/g).map((b) => parseInt(b, 2)))
  const counter = Buffer.alloc(8)
  counter.writeBigInt64BE(BigInt(Math.floor(Date.now() / 30000) + stepOffset))
  const hash = createHmac('sha1', key).update(counter).digest()
  const offset = hash[hash.length - 1] & 0x0f
  return String((hash.readUInt32BE(offset) & 0x7fffffff) % 1_000_000).padStart(6, '0')
}

/** One browser: a cookie jar, antiforgery and idempotency headers, and the active organization. */
export class Client {
  cookies = new Map()
  org = null
  sharedKey = null

  async send(method, path, body) {
    // The antiforgery call sets its cookie first, so the cookie header is built after it.
    const xsrf = method === 'GET' ? null : (await this.send('GET', '/api/v1/auth/antiforgery')).token
    const headers = { Cookie: [...this.cookies].map(([k, v]) => `${k}=${v}`).join('; ') }
    if (xsrf) {
      headers['X-XSRF-TOKEN'] = xsrf
      headers['Idempotency-Key'] = randomUUID()
    }
    if (this.org) headers['X-Organization-Id'] = this.org
    let payload
    if (body instanceof FormData) payload = body
    else if (method !== 'GET') {
      headers['Content-Type'] = 'application/json'
      payload = JSON.stringify(body ?? {})
    }
    const response = await fetch(base + path, { method, headers, body: payload, redirect: 'manual' })
    for (const cookie of response.headers.getSetCookie()) {
      const [pair] = cookie.split(';')
      const at = pair.indexOf('=')
      this.cookies.set(pair.slice(0, at), pair.slice(at + 1))
    }
    const text = await response.text()
    if (!response.ok) throw new Error(`${method} ${path}: ${response.status} ${text.slice(0, 300)}`)
    return text ? JSON.parse(text) : null
  }

  async signIn(email, password, sharedKey) {
    this.sharedKey = sharedKey
    await this.send('POST', '/api/v1/auth/login', { email, password })
    await this.send('POST', '/api/v1/auth/mfa/verify', { code: totp(sharedKey) })
    return this
  }

  stepUp() {
    return this.send('POST', '/api/v1/auth/step-up', { code: totp(this.sharedKey) })
  }
}
