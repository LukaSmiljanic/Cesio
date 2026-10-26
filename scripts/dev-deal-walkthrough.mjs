// Takes an offer of the demo accounts through a whole deal on the local stack and prints what each step changes:
// acceptance, CDD, contract, terms, signatures (test signatures for the local fake validator), funding report,
// confirmation and outcome. Resumable: an accepted offer continues its deal from the state it is in.
// Usage: node scripts/dev-deal-walkthrough.mjs <offerId> [stopAfterStep]
import { readFileSync } from 'node:fs'
import { randomUUID } from 'node:crypto'
import { Client } from './dev-lib.mjs'

const [offerId, stopAfter = '99'] = process.argv.slice(2)
if (!offerId) throw new Error('Usage: node scripts/dev-deal-walkthrough.mjs <offerId> [stopAfterStep]')
const accounts = JSON.parse(readFileSync(new URL('../.dev-accounts.local.json', import.meta.url), 'utf8'))
const password = readFileSync(new URL('../dev-accounts.local.md', import.meta.url), 'utf8').match(/Lozinka za sve naloge: `([^`]+)`/)[1]
const signIn = async (prefix) => {
  const a = accounts.find((x) => x.email.startsWith(prefix))
  const client = await new Client().signIn(a.email, password, a.sharedKey)
  client.org = (await client.send('GET', '/api/v1/me/organizations'))[0]?.id ?? null
  await client.stepUp()
  return client
}
const pdf = (lines) => {
  const form = new FormData()
  form.append('file', new Blob([`%PDF-1.7\n${lines.join('\n')}\n%${randomUUID()}\n%%EOF\n`], { type: 'application/pdf' }), 'ugovor.pdf')
  form.append('purpose', 'Contract')
  return form
}
// The fake validator reads the PDF as Latin-1, so letters such as ć travel as JSON \u escapes.
const ascii = (text) => text.replace(/[^\x20-\x7e]/g, (c) => '\\u' + c.charCodeAt(0).toString(16).padStart(4, '0'))
const signature = (signer, covers) =>
  `%CESIO-FAKE-SIGNATURE ${ascii(JSON.stringify({ signer, covers, signingTime: new Date().toISOString() }))}`
const today = new Date().toISOString().slice(0, 10)

const seller = await signIn('prodavac-')
const factor = await signIn('faktor-')
let step = 0
async function show(title, dealId) {
  step++
  const d = await seller.send('GET', `/api/v1/deals/${dealId}`)
  console.log(`\n${step}. ${title}`)
  console.log(`   stanje posla: ${d.deal.state}${d.steps.waitingFor ? ` · na potezu: ${d.steps.waitingFor}` : ''}`)
  if (step >= Number(stopAfter)) {
    console.log('\n(zaustavljeno po zahtevu)')
    process.exit(0)
  }
  return d
}

let offer = null
let before = []
for (const listing of await seller.send('GET', '/api/v1/listings')) {
  const offers = await seller.send('GET', `/api/v1/listings/${listing.id}/offers`)
  const match = offers.find((o) => o.id.toLowerCase() === offerId.toLowerCase())
  if (match) [offer, before] = [{ ...match, listingId: listing.id }, offers]
}
if (!offer) throw new Error('The offer is not on a listing of the demo seller.')
console.log(`Ponuda: ${offer.purchasePrice.amount} RSD, ostale ponude na oglasu: ${before.length - 1}`)

let dealId
let d1
if (offer.state === 'Active') {
  const accepted = await seller.send('POST', `/api/v1/offers/${offerId}/accept`, { acknowledgements: { netProceedsShown: true, termsVersion: null } })
  dealId = accepted.dealId
  d1 = await show(`Prodavac prihvata ponudu → nastaje posao ${accepted.reference}`, dealId)
  const after = await seller.send('GET', `/api/v1/listings/${offer.listingId}/offers`)
  console.log(`   ponude na oglasu posle prihvatanja: ${after.map((o) => o.state).join(', ')}`)
} else {
  const deal = (await seller.send('GET', '/api/v1/deals')).find((d) => d.offerId.toLowerCase() === offerId.toLowerCase())
  if (!deal) throw new Error(`The offer is ${offer.state} and has no deal.`)
  dealId = deal.id
  d1 = await seller.send('GET', `/api/v1/deals/${dealId}`)
  console.log(`Posao ${deal.reference} već postoji, stanje ${deal.state}; nastavljam odatle.`)
}
console.log(`   kupoprodajna cena ${d1.deal.purchasePrice.amount}, prodavac dobija ${d1.deal.sellerNetProceeds.amount}, naknada platforme (plaća faktor) ${d1.deal.platformFee.amount}`)
const state = async () => (await seller.send('GET', `/api/v1/deals/${dealId}`)).deal.state

if ((await state()) === 'AwaitingFactorCdd') {
  await factor.send('POST', `/api/v1/deals/${dealId}/cdd-decision`, { decision: 'Approved' })
  await show('Faktor završava proveru prodavca (CDD) i odobrava posao', dealId)
}

if ((await state()) === 'AwaitingContract') {
  const uploaded = await factor.send('POST', '/api/v1/documents', pdf(['ugovor o faktoringu (test)']))
  const contract = await factor.send('POST', `/api/v1/deals/${dealId}/contract`, { documentId: uploaded.id })
  await show('Faktor kači ugovor o faktoringu', dealId)
  await seller.send('POST', `/api/v1/deals/${dealId}/terms-confirmation`, { contractDocumentId: contract.contractDocumentId, confirmed: true })
  await factor.send('POST', `/api/v1/deals/${dealId}/terms-confirmation`, { contractDocumentId: contract.contractDocumentId, confirmed: true })
  await show('Obe strane potvrđuju da ugovor odgovara uslovima', dealId)
}

if ((await state()) === 'AwaitingSignatures') {
  const agreed = (await seller.send('GET', `/api/v1/deals/${dealId}`)).deal.agreedContractSha256
  const signed = await factor.send('POST', '/api/v1/documents', pdf([signature('Jelena Jovanović', agreed), signature('Petar Petrović', agreed)]))
  const signing = await factor.send('POST', `/api/v1/deals/${dealId}/signed-contract`, { documentId: signed.id })
  await show('Zastupnici potpisuju ugovor (test potpisi), platforma proverava potpise', dealId)
  console.log(`   potpisi: ${signing.signatures.map((x) => `${x.signer} → ${x.result}`).join('; ')}`)
}

const price = d1.deal.purchasePrice
if ((await state()) === 'Signed') {
  const [account] = await factor.send('GET', `/api/v1/deals/${dealId}/funding-accounts`)
  await factor.send('POST', `/api/v1/deals/${dealId}/funding-report`, { amount: price, valueDate: today, paymentReference: `97-${Date.now() % 100000}`, payeeBankAccountId: account.id })
  await show(`Faktor uplaćuje ${price.amount} RSD na proveren račun prodavca (${account.accountNumber}) i prijavljuje uplatu`, dealId)
}

if ((await state()) === 'FundingReported') {
  await seller.send('POST', `/api/v1/deals/${dealId}/funding-confirmation`, { received: true, reason: null })
  await show('Prodavac potvrđuje da je novac stigao → posao je finansiran', dealId)
  await new Promise((resolve) => setTimeout(resolve, 4000))
  const charge = (await factor.send('GET', '/api/v1/fees/charges')).find((c) => c.dealId === dealId)
  console.log(charge ? `   naknada platforme obračunata faktoru: ${charge.amount.amount} RSD, status ${charge.status}` : '   naknadu obračunava worker (pogledaj Naknade kod faktora)')
}

if ((await state()) === 'Funded') {
  await factor.send('POST', `/api/v1/deals/${dealId}/outcome`, { type: 'Settled', date: today, amountCollected: d1.deal.faceValue })
  await show('Dužnik je platio faktoru; faktor upisuje ishod → posao je zatvoren', dealId)
}
