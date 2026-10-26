// Puts two of the demo seller's draft receivables on the marketplace and gives the first one an offer from the demo
// factor, so the seller's offer comparison and the factor's marketplace have something to show. Run after dev-seed.mjs.
// Usage: node scripts/dev-seed-market.mjs
import { readFileSync } from 'node:fs'
import { Client } from './dev-lib.mjs'

const accounts = JSON.parse(readFileSync(new URL('../.dev-accounts.local.json', import.meta.url), 'utf8'))
const password = readFileSync(new URL('../dev-accounts.local.md', import.meta.url), 'utf8').match(/Lozinka za sve naloge: `([^`]+)`/)[1]
const account = (prefix) => accounts.find((a) => a.email.startsWith(prefix))
const signIn = async (prefix) => {
  const a = account(prefix)
  const client = await new Client().signIn(a.email, password, a.sharedKey)
  client.org = (await client.send('GET', '/api/v1/me/organizations'))[0]?.id ?? null
  return client
}

const seller = await signIn('prodavac-')
// The fake SEF lives in the API's memory: run this right after dev-seed.mjs, before the API restarts (after a restart,
// run both again for a fresh set of accounts).
const drafts = (await seller.send('GET', '/api/v1/receivables')).filter((r) => r.state === 'Draft').slice(0, 2)
if (drafts.length === 0) throw new Error('The demo seller has no draft receivables; run dev-seed.mjs first.')

for (const receivable of drafts) {
  await seller.send('PUT', `/api/v1/receivables/${receivable.id}/declaration`, {
    answers: { NoPriorAssignment: true, NoAssignmentProhibition: true, NoSetOffOrDispute: true, GoodsOrServicesDelivered: true },
  })
  const form = new FormData()
  form.append('file', new Blob(['%PDF-1.7\nugovor i otpremnica\n%%EOF\n'], { type: 'application/pdf' }), 'otpremnica.pdf')
  form.append('purpose', 'ReceivableEvidence')
  const document = await seller.send('POST', '/api/v1/documents', form)
  await seller.send('POST', `/api/v1/receivables/${receivable.id}/documents`, { documentId: document.id })
  await seller.send('POST', `/api/v1/receivables/${receivable.id}/submit`)
}

const admin = await signIn('admin-')
admin.org = null
await admin.stepUp()
// The automatic checks run in the worker; a receivable either verifies itself or waits for the officer's decision.
for (const receivable of drafts) {
  for (let attempt = 0; ; attempt++) {
    const state = (await seller.send('GET', `/api/v1/receivables/${receivable.id}`)).state
    if (state !== 'PendingVerification') break
    try {
      await admin.send('POST', `/api/v1/admin/receivables/${receivable.id}/decision`, { decision: 'Verified' })
      break
    } catch (error) {
      if (attempt > 40 || !String(error.message).includes('409')) throw error
      await new Promise((resolve) => setTimeout(resolve, 3000))
    }
  }
}

const closesAt = new Date(Date.now() + 10 * 86400000).toISOString()
const listings = []
for (const receivable of drafts) {
  if ((await seller.send('GET', `/api/v1/receivables/${receivable.id}`)).state !== 'Verified') continue
  listings.push((await seller.send('POST', `/api/v1/receivables/${receivable.id}/listings`, { closesAt })).id)
}

const factor = await signIn('faktor-')
if (listings.length === 0) throw new Error('No receivable reached Verified; check the worker and the admin queue.')
const face = Number(drafts[0].faceValue.amount)
const price = (Math.round(face * 0.96 * 100) / 100).toFixed(2)
await factor.send('POST', `/api/v1/marketplace/listings/${listings[0]}/offers`, {
  purchasePrice: { amount: price, currency: 'RSD' },
  recourseType: 'NonRecourse',
  fundingDeadlineBusinessDays: 2,
  validUntil: new Date(Date.now() + 5 * 86400000).toISOString(),
  conditions: 'Standardni ugovor o faktoringu',
})

console.log(`Listed ${listings.length} receivables; one offer of ${price} RSD from the demo factor.`)
