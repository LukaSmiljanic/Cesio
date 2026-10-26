// Local demo accounts for a running Development stack (docker compose, API, web): a seller with three SEF invoices
// imported, an authorized factor, and two staff users (four-eyes needs two people). Only for localhost: it uses the
// /dev endpoints and writes to the local database directly where a flow needs an officer outside the app.
// Usage: node scripts/dev-seed.mjs   (the web dev server proxies the API; set CESIO_URL if it is not on :5174)
// Writes dev-accounts.local.md and .dev-accounts.local.json (both gitignored); TOTP codes: node scripts/dev-totp.mjs
import { execFileSync } from 'node:child_process'
import { createHmac, randomBytes, randomUUID } from 'node:crypto'
import { readFileSync, writeFileSync } from 'node:fs'

const base = process.env.CESIO_URL ?? 'http://localhost:5174'
const mailbox = process.env.CESIO_SMTP4DEV ?? 'http://localhost:5000'
const host = new URL(base).hostname
if (!['localhost', '127.0.0.1'].includes(host)) throw new Error(`Only for a local stack, not ${host}.`)

const password = `Demo-${randomBytes(9).toString('base64url')}-1`
const run = Date.now().toString(36)
const buyerPib = '106006802'

function totp(base32Key, stepOffset = 0) {
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

function newPib() {
  for (;;) {
    const prefix = String(1 + Math.floor(Math.random() * 9)) + Array.from({ length: 7 }, () => Math.floor(Math.random() * 10)).join('')
    let product = 10
    for (const d of prefix) {
      const sum = (Number(d) + product) % 10
      product = (2 * (sum === 0 ? 10 : sum)) % 11
    }
    return prefix + ((11 - product) % 10)
  }
}

function newBankAccount() {
  const body = '160' + Array.from({ length: 13 }, () => Math.floor(Math.random() * 10)).join('')
  return body + String(98 - Number(BigInt(body + '00') % 97n)).padStart(2, '0')
}

function sql(statement) {
  const env = readFileSync(new URL('../.env', import.meta.url), 'utf8')
  const sa = env.match(/^MSSQL_SA_PASSWORD=(.*)$/m)[1].trim()
  try {
    execFileSync('docker', ['exec', 'cesio-sqlserver-1', '/opt/mssql-tools18/bin/sqlcmd', '-C', '-b', '-I', '-S', 'localhost', '-U', 'sa', '-P', sa, '-d', 'cesio', '-Q', statement], { stdio: 'pipe' })
  } catch (error) {
    // Only sqlcmd's own output: the command line holds the SA password.
    throw new Error(`SQL failed: ${String(error.stdout ?? '')}${String(error.stderr ?? '')}`.trim())
  }
}

class Client {
  cookies = new Map()
  org = null

  async send(method, path, body) {
    // The antiforgery call sets its cookie first, so the cookie header is built after it.
    const xsrf = method === 'GET' ? null : (await this.send('GET', '/api/v1/auth/antiforgery')).token
    const headers = { Cookie: [...this.cookies].map(([k, v]) => `${k}=${v}`).join('; ') }
    if (xsrf) {
      headers['X-XSRF-TOKEN'] = xsrf
      headers['Idempotency-Key'] = randomUUID()
      headers['Content-Type'] = 'application/json'
    }
    if (this.org) headers['X-Organization-Id'] = this.org
    const response = await fetch(base + path, { method, headers, body: method === 'GET' ? undefined : JSON.stringify(body ?? {}), redirect: 'manual' })
    for (const cookie of response.headers.getSetCookie()) {
      const [pair] = cookie.split(';')
      const at = pair.indexOf('=')
      this.cookies.set(pair.slice(0, at), pair.slice(at + 1))
    }
    const text = await response.text()
    if (!response.ok) throw new Error(`${method} ${path}: ${response.status} ${text.slice(0, 300)}`)
    return text ? JSON.parse(text) : null
  }

  async signUp(email) {
    await this.send('POST', '/api/v1/auth/register', { email, password })
    const link = await confirmationLink(email)
    await this.send('POST', '/api/v1/auth/confirm-email', { userId: link.get('userId'), token: link.get('token') })
    await this.send('POST', '/api/v1/auth/login', { email, password })
    this.sharedKey = (await this.send('POST', '/api/v1/auth/mfa/enrol/start')).sharedKey
    await this.send('POST', '/api/v1/auth/mfa/enrol/confirm', { code: totp(this.sharedKey) })
    return this
  }

  stepUp() {
    return this.send('POST', '/api/v1/auth/step-up', { code: totp(this.sharedKey) })
  }
}

async function confirmationLink(email) {
  for (let attempt = 0; attempt < 20; attempt++) {
    const list = await (await fetch(`${mailbox}/api/messages?pageSize=50`)).json()
    const message = (list.results ?? list).find((m) => (m.to ?? []).some((t) => String(t).includes(email)))
    if (message) {
      const text = await (await fetch(`${mailbox}/api/messages/${message.id}/plaintext`)).text()
      const url = text.match(/https?:\/\/\S+confirm-email\?\S+/)[0]
      return new URL(url).searchParams
    }
    await new Promise((resolve) => setTimeout(resolve, 500))
  }
  throw new Error(`No confirmation email for ${email} in smtp4dev (${mailbox}).`)
}

const accounts = []

// Seller with a verified payout account, a SEF connection and three invoices imported as drafts.
const seller = await new Client().signUp(`prodavac-${run}@demo.cesio.local`)
const sellerPib = newPib()
seller.org = (await seller.send('POST', '/api/v1/organizations', { pib: sellerPib })).id
await seller.send('POST', `/api/v1/organizations/${seller.org}/legal-representatives`, { name: 'Petar Petrović', role: 'Direktor' })
await seller.send('POST', `/api/v1/dev/organizations/${seller.org}/approve`)
sql(`INSERT INTO orgs.bank_accounts (id, org_id, account_number, currency, bank_name, status, duplicate_flag, verified_at, usable_from, created_by, created_at, version)
     VALUES (NEWID(), '${seller.org}', '${newBankAccount()}', 'RSD', 'Banca Intesa', 'Verified', 0, DATEADD(day, -4, SYSDATETIMEOFFSET()), DATEADD(day, -1, SYSDATETIMEOFFSET()), NEWID(), DATEADD(day, -5, SYSDATETIMEOFFSET()), 0)`)
const sefKey = `demo-${randomUUID()}`
const due = (days) => new Date(Date.now() + days * 86400000).toISOString().slice(0, 10)
for (const [number, net, days] of [['F-2026-101', 1_000_000, 60], ['F-2026-102', 2_500_000, 75], ['F-2026-103', 750_000, 90]]) {
  await seller.send('POST', '/api/v1/dev/sef/invoices', { apiKey: sefKey, supplierPib: sellerPib, buyerPib, number: `${number}-${run}`, net, vat: net * 0.2, dueDate: due(days), status: 'Approved' })
}
await seller.stepUp()
await seller.send('PUT', `/api/v1/organizations/${seller.org}/sef-connection`, { apiKey: sefKey })
await seller.send('POST', '/api/v1/receivables/import-from-sef', { issueDateFrom: due(-1), issueDateTo: due(0) })
accounts.push({ role: 'Prodavac (ustupilac)', email: `prodavac-${run}@demo.cesio.local`, sharedKey: seller.sharedKey, note: `PIB ${sellerPib}; 3 fakture uvezene iz (lažnog) SEF-a kao nacrti` })

// Factor with a verified authorization (set directly; the two-officer flow is in the app).
const factor = await new Client().signUp(`faktor-${run}@demo.cesio.local`)
const factorPib = newPib()
factor.org = (await factor.send('POST', '/api/v1/dev/factor-organizations', { pib: factorPib, legalName: 'Demo Faktor a.d.' })).id
await factor.send('POST', `/api/v1/organizations/${factor.org}/legal-representatives`, { name: 'Jelena Jovanović', role: 'Direktor' })
await factor.send('POST', `/api/v1/dev/organizations/${factor.org}/approve`)
sql(`INSERT INTO compliance.factor_authorizations (id, org_id, kind, issuing_authority, status, verified_at, reverify_by, submitted_by, created_at, version)
     VALUES (NEWID(), '${factor.org}', 'Bank', 'NationalBankOfSerbia', 'Verified', SYSDATETIMEOFFSET(), DATEADD(day, 30, CAST(GETDATE() AS date)), NEWID(), SYSDATETIMEOFFSET(), 0)`)
accounts.push({ role: 'Faktor', email: `faktor-${run}@demo.cesio.local`, sharedKey: factor.sharedKey, note: `Demo Faktor a.d., PIB ${factorPib}, ovlašćenje verifikovano` })

// Two staff users, so four-eyes actions (fee reversals, money parameters, FraudInvestigation holds) can be tried.
for (const [name, roles] of [['admin', ['ComplianceOfficer', 'Operations', 'Finance', 'PlatformAdmin']], ['admin2', ['ComplianceOfficer', 'Finance', 'Auditor']]]) {
  const email = `${name}-${run}@demo.cesio.local`
  const staff = await new Client().signUp(email)
  for (const role of roles) await staff.send('POST', '/api/v1/dev/me/platform-roles', { role })
  accounts.push({ role: `Zaposleni: ${roles.join(', ')}`, email, sharedKey: staff.sharedKey, note: name === 'admin' ? 'prvi potpis za četiri oka' : 'drugi potpis za četiri oka' })
}

writeFileSync(new URL('../.dev-accounts.local.json', import.meta.url), JSON.stringify(accounts, null, 2))
writeFileSync(
  new URL('../dev-accounts.local.md', import.meta.url),
  `# Lokalni demo nalozi (${new Date().toISOString().slice(0, 16)})

Samo za lokalni razvoj. Otvori ${base}.

Lozinka za sve naloge: \`${password}\`

MFA kod: \`node scripts/dev-totp.mjs\` ispisuje trenutne kodove svih naloga. Ili dodaj tajni ključ u aplikaciju za autentifikaciju.

| Uloga | Email | TOTP tajni ključ | Napomena |
|---|---|---|---|
${accounts.map((a) => `| ${a.role} | ${a.email} | \`${a.sharedKey}\` | ${a.note} |`).join('\n')}

Mejlovi (potvrde, obaveštenja): ${mailbox}
`,
)
console.log(`Created ${accounts.length} accounts; see dev-accounts.local.md`)
