---
name: cesio-invoice-state-change
description: Koristi u Cesio projektu kad dodaješ, menjaš ili brišeš stanje ili prelaz potraživanja (Receivable), listinga, ponude ili posla (Deal), npr. novo stanje ili nova tranzicija "povučena". Ne koristi za izmene koje samo čitaju stanje.
---

# Izmena state machine-a

Faktura (Invoice) je nepromenljiv dokaz iz SEF-a i nema životni ciklus. Stanja imaju **Receivable, Listing, Offer i Deal** (ADR-012). Izvor istine je `docs/product-spec.md` §7; prelazi i uslovi su u `docs/domain-model.md` §6.

**Stanja i invarijante se ne menjaju bez izričite instrukcije korisnika** (`CLAUDE.md`). Prvo predloži izmenu dokumentacije.

## Trenutni tok (ažuriraj kad se promeni, zajedno sa product-spec §7)
```
Receivable: Draft → PendingVerification → Verified → Listed → Reserved → Assigned → Funded
            → Settled | RecourseReturned | Defaulted
            bočno: Rejected, Ineligible, Cancelled; Listed/Reserved → Verified; Assigned → Verified (uz dokaz, LQ-25)
Listing:    Open → Closed | Withdrawn | Expired | Suspended (→ Open | Withdrawn)
Offer:      Active → Accepted | Withdrawn | Rejected | Expired | Superseded   (ponude su nepromenljive, ADR-013)
Deal:       AwaitingFactorCdd → AwaitingContract → AwaitingSignatures → Signed → FundingReported → Funded → Closed
            bočno: Cancelled, Terminated (Funded se nikad ne terminira, INV-R6)
Hold (sloj, nije stanje, ADR-014): None | ComplianceHold | Disputed | FraudInvestigation
```
Definicija stanja u kodu: [POPUNITI: putanja kad moduli Receivables/Marketplace/Deals dobiju domenski model (taskovi 011–017)].

## Koraci
1. **Definicija:** stanje ili prelaz se dodaje na JEDNOM mestu, u agregatu koji ga poseduje (`docs/domain-model.md` §1). Prelazi su eksplicitne metode (`Offer.Accept()`), ne slobodno postavljanje polja.
2. **Uslovi (guards):** ko sme (uloga, Signatory, MFA step-up) i pod kojim uslovom (invarijante INV-R, INV-A, INV-F). Aktivan Hold blokira svaki prelaz unapred.
3. **Efekti:** domenski događaj kroz outbox (`docs/domain-model.md` §7), obaveštenja, obračun. Efekti koji pozivaju spoljne sisteme moraju biti idempotentni.
4. **Audit:** svaki prelaz u istoj transakciji upisuje audit događaj sa `previous_state`, `new_state`, akterom, vremenom i razlogom (`docs/audit.md` §2).
5. **Konkurentnost:** optimistic concurrency (`version`) na svakom agregatu koji prelaz dira (INV-F4).
6. **Migracija:** ako se menja CHECK konstraint stanja ili postojeći zapisi prelaze u novo stanje, koristi `cesio-migration`.
7. **Testovi:** za svaki nov prelaz jedan test da je dozvoljen i bar jedan da je zabranjen iz pogrešnog stanja, za pogrešnu ulogu i dok je aktivan Hold.
8. **Dokumentacija:** ažuriraj tok iznad, `docs/product-spec.md` §7 i `docs/domain-model.md` §6, pa pokreni `doc-consistency`.

Ako novo stanje ima pravne posledice (npr. prelazak vlasništva, obaveštenje dužnika), proveri `docs/legal-questions.md` i pozovi `legal-review`.
