// Current TOTP codes of the local demo accounts written by scripts/dev-seed.mjs.
// Usage: node scripts/dev-totp.mjs [part of an email]
import { readFileSync } from 'node:fs'
import { createHmac } from 'node:crypto'

function totp(base32Key) {
  const alphabet = 'ABCDEFGHIJKLMNOPQRSTUVWXYZ234567'
  let bits = ''
  for (const c of base32Key.replace(/[\s=]/g, '').toUpperCase()) bits += alphabet.indexOf(c).toString(2).padStart(5, '0')
  const key = Buffer.from(bits.match(/.{8}/g).map((b) => parseInt(b, 2)))
  const counter = Buffer.alloc(8)
  counter.writeBigInt64BE(BigInt(Math.floor(Date.now() / 30000)))
  const hash = createHmac('sha1', key).update(counter).digest()
  const offset = hash[hash.length - 1] & 0x0f
  return String((hash.readUInt32BE(offset) & 0x7fffffff) % 1_000_000).padStart(6, '0')
}

const filter = process.argv[2] ?? ''
const accounts = JSON.parse(readFileSync(new URL('../.dev-accounts.local.json', import.meta.url), 'utf8'))
const left = 30 - (Math.floor(Date.now() / 1000) % 30)
for (const a of accounts.filter((a) => a.email.includes(filter))) console.log(`${totp(a.sharedKey)}  ${a.email}`)
console.log(`(važi još ${left} s)`)
