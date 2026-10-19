# UX & Visual Design

Status: **draft v0.1** (2026-10-05). The prototype is a validation tool for the task 000 interviews, not a final design.
Prototype: `design/prototype.html` (single-file, clickable, fictional data).

## 1. Users and context
| User | Context | What they need from the UI |
|------|---------|----------------------------|
| Seller finance staff (accountant, CFO of an SME) | Desktop, a few sessions per month, not factoring experts | Clear steps, plain-language explanations, a clear **net amount** and cost, confidence that nothing irreversible happens by accident |
| Seller signatory (director) | Often mobile, short sessions | Approve or accept with full context on one screen; strong confirmation |
| Factor analyst | Desktop, daily, high volume | Dense, filterable lists; fast access to debtor, SEF status and documents; quick offer entry |
| Factor signatory | Desktop/mobile | Deal step overview, funding report |
| Platform compliance/ops | Desktop, all day | Queues, evidence side by side, four-eyes flows (separate admin app) |

Language: **Serbian (Latin)** by default, English as the second language. Cyrillic UI is a future option; search must match Latin↔Cyrillic.

## 2. UX principles (in priority order)
1. **Money is unambiguous.** Every amount shows its currency, uses Serbian formatting (`11.720.000,00 RSD`) and tabular numerals. The seller always sees the **net amount they receive**. Derived rates are labelled "indicative" (LQ-04).
2. **"Who acts now" is always visible.** Every deal and receivable shows the current step, who is responsible (you / counterparty / platform), and the deadline.
3. **Nothing irreversible without confirmation.** Acceptance, signing, funding and outcome actions use a summary dialog plus step-up MFA. Button labels say exactly what happens ("Prihvati ponudu i kreiraj posao"), never "OK".
4. **Trust through visible verification.** The SEF status and when it was last checked, KYB status, factor authorization and signature validation results are shown where decisions are made, not hidden in settings.
5. **Dense but calm.** Lists are tables with sortable, aligned numbers; detail pages lead with a summary and keep the history below. Colour encodes state, never decoration.
6. **Explain the legal step, don't lecture.** One-line explanations at each step (e.g. what the CEF is and why it shows "Not applicable"), linking to help. No legal conclusions in UI copy beyond what [regulatory.md](regulatory.md) confirms.
7. **Mobile = approve and monitor; desktop = prepare.** Import, declarations and offer entry are desktop-first but still work on mobile. Approvals and statuses are designed mobile-first.

## 3. Information architecture
**Seller:** Pregled (dashboard) · Potraživanja (receivables: import, declarations, listings) · Ponude (offers across listings) · Poslovi (deals) · Naknade (fees; only if the seller pays) · Organizacija (company, members, bank accounts, SEF connection, KYB).

**Factor:** Pregled · Tržište (marketplace) · Moje ponude · Poslovi · Naknade · Organizacija (incl. authorization, appetite filters).

**Admin (separate app):** Redovi čekanja (KYB, authorizations, receivables, bank accounts, disputes) · Organizacije · Poslovi · Naknade · Parametri · Audit · Integracije.

Global: organization switcher (users can be members of several orgs), notifications, profile/security (sessions, MFA).

## 4. Key screens (MVP)
| # | Screen | Role | Notes |
|---|--------|------|-------|
| S1 | Dashboard | S/F | Action-needed list first, then KPIs |
| S2 | Receivables list | S | State chips, filters, "Uvezi iz SEF-a" |
| S3 | SEF import dialog | S | Per-item results (Imported / Already imported / Not issuer / Unsupported) |
| S4 | Receivable detail | S | Invoice summary, SEF status, declarations, documents, verification checks, listing |
| S5 | Publish listing dialog | S | Audience, exclusions, closing time, optional reserve price |
| S6 | Offer comparison | S | Columns: factor, purchase price, discount %, **net to you**, indicative annual cost, recourse, funding term, valid until. Sorted by net amount |
| S7 | Accept offer dialog | S | Summary + consequences (other offers close, receivable reserved) + step-up |
| S8 | Deal page / timeline | S/F | Stepper with responsible party and deadline; documents; signatures; funding; history |
| S9 | Marketplace feed | F | Table with filters (amount, days to maturity, debtor); own offer indicator |
| S10 | Listing detail + offer form | F | Debtor, SEF status, documents, seller KYB status; live offer metrics incl. platform fee |
| S11 | Funding report / confirmation dialogs | F/S | Verified account shown; amount must match |
| S12 | Organization onboarding wizard | S/F | Seller: PIB → SEF → company data → payout account → pristupnica → review (task 007a, ADR-020). Factor: invitation → company data → authorization → users |
| S13 | Factor authorization form | F | Evidence upload, status, re-verification date |
| S14 | Notifications | all | |
| S15 | Security (sessions, MFA) | all | |

The prototype covers S1, S2, S3, S4 (light), S5, S6, S7, S8, S9, S10, S11 and the step-up dialog.

## 5. State presentation
| Group | States (UI label) | Visual |
|-------|-------------------|--------|
| Neutral/in progress | Nacrt, Na proveri, Verifikovano | Neutral chip |
| Market | Na tržištu, Rezervisano | Accent chip |
| Done/positive | Ustupljeno, Isplaćeno, Naplaćeno | Positive chip |
| Attention | Čeka vašu akciju, Rok ističe | Warning chip + action button |
| Negative/terminal | Odbijeno, Neprihvatljivo, Otkazano | Critical chip (Otkazano is neutral) |
| Overlay | Zadržano (Hold), Spor | Striped badge next to the state chip |

## 6. Formatting & content rules
- Amounts: `11.720.000,00 RSD`. The currency code always comes after the amount; never abbreviate millions in financial tables (allowed in KPI tiles: `11,7 mil. RSD`).
- Dates: `03.01.2027.`; date-time `05.10.2026. 09:14`; relative time only as a secondary hint ("za 2 dana").
- Percentages: 2 decimals for rates (`12,54 %`).
- Identifiers: PIB, MB and SEF IDs are monospaced and copyable.
- Tone: formal "Vi" form in Serbian, short sentences, verbs on buttons.
- Errors say what happened and how to fix it ("SEF trenutno nije dostupan. Status fakture nije proveren, pa objavljivanje nije moguće. Pokušajte ponovo za nekoliko minuta.").

## 7. Visual direction (prototype v0.1)
- Character: a calm, precise "ledger" look. Ink-blue neutrals with a slight cool bias, one deep teal accent, semantic colours for state only.
- Type: Bricolage Grotesque (display, used sparingly), IBM Plex Sans (UI/body, full Serbian Latin support), IBM Plex Mono (identifiers, amounts in tables).
- Light and dark themes are both supported via tokens.
- **Brand (name, logo, final palette) is open.** A designer engagement is recommended before public launch (BQ: brand).

## 8. Design system & component library (ADR-010)
Proposed: **shadcn/ui (Radix primitives) + Tailwind CSS + TanStack Table + react-hook-form + zod**.
Why: component source lives in our repo (full control, easy for AI agents to modify consistently), accessible primitives, no heavy theme override, and strong data-table support for factor and admin screens.
Alternatives: Mantine (fastest start), MUI, Ant Design. See the comparison in the conversation of 2026-10-05.
Tokens from §7 become Tailwind theme variables. The prototype's CSS custom properties are the starting token set.

Accessibility target: **WCAG 2.1 AA** (contrast, keyboard navigation, focus visible, form labels, error association, reduced motion).

## 9. Validation plan
1. Show the prototype in task 000 interviews: 5 sellers and 5 factors, 30 minutes each.
2. Scripted tasks:
   - Seller: "Find the best offer and explain what you will receive"; "Confirm receipt of funds".
   - Factor: "Find invoices maturing in under 100 days and make an offer".
3. Measure: task success, time, misunderstandings of amounts or states. Collect what information factors are missing to price an offer.
4. Feed the results into this document (v0.2) before task 004.

## 10. Open design questions
- D-01: Offer comparison: show the indicative annual cost at all (LQ-04)? The prototype shows it, labelled as indicative.
- D-02: Should the debtor name be visible in the marketplace feed or only in the detail view (BQ-06)?
- D-03: Mobile signing flow when signatures are made externally with QES.
- D-04: Cyrillic UI needed at launch?
- D-05: Brand and name.
