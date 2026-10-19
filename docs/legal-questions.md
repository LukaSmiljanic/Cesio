# Legal & Tax Questions — Pre-Production Checklist

Every question below is `LEGAL QUESTION — REQUIRES PROFESSIONAL REVIEW` unless it is marked *(tax)*, which means it goes to a tax advisor or accountant.
**Production launch is blocked** until every **P0** question is answered in writing and the answer is reflected in
[regulatory.md](regulatory.md) and [decisions.md](decisions.md).

Priority: **P0** = blocks MVP launch; **P1** = blocks a specific feature; **P2** = before scale or expansion.

Answer format: answer · source / reasoning · lawyer · date · resulting doc/ADR change.

## A. Platform status & licensing
| ID | P | Question |
|----|---|----------|
| LQ-01 | P0 | Does operating a marketplace that matches sellers with factors require any licence, approval or registration (Securities Commission, NBS, Ministry of Finance)? Is it "intermediation in factoring", and is that a regulated activity? |
| LQ-02 | P0 | Can the platform be characterised as "performing factoring" in any way (e.g. by standardising terms, ranking offers, or hosting the contract)? Which features would cross that line? |
| LQ-03 | P0 | Is a transaction-based success fee (a % of the receivable's face value), charged to the factor and/or seller, permitted? Any caps, disclosure or "conflict of interest" rules? |
| LQ-04 | P1 | May the platform show offer comparisons, "best offer" ranking or an effective annual cost to sellers without being a financial adviser or credit intermediary? |
| LQ-05 | P2 | Would a future platform-owned purchasing vehicle (balance-sheet participation) require a separate factoring approval and conflict-of-interest separation? |

## B. Payments
| ID | P | Question |
|----|---|----------|
| LQ-06 | P0 | Confirm that a model where the **factor pays the seller directly** and the platform only records confirmations keeps the platform outside the Law on Payment Services. |
| LQ-07 | P1 | Could the platform ever initiate payments (e.g. a payment-initiation service, or generating NBS IPS QR payment orders) without a licence? |
| LQ-08 | P2 | Would an escrow or collection account (platform or partner bank) require a licence or a bank partnership structure? |

## C. Factoring law specifics
| ID | P | Question |
|----|---|----------|
| LQ-09 | P0 | Which public source is authoritative for verifying a factoring company's approval after the Securities Commission took over (2026)? Do Ministry of Finance approvals issued before the transfer remain valid without action? *Research 2026-10-17:* the APR Registar faktoringa (Art. 10) is public and searchable (regulatory.md S9). Remaining questions: is an APR entry or certificate sufficient evidence on its own, and how quickly are new Securities Commission approvals recorded there? |
| LQ-10 | P0 | Exact definition of "short-term" receivable (maximum tenor) and whether a receivable with a past-due date is ever eligible. |
| LQ-11 | P1 | Can foreign factors (no Serbian approval) buy domestic receivables via the platform? |
| LQ-12 | P1 | Partial assignment: may two factors each acquire a different portion of one invoice? How does Art. 22 (prohibition of multiple agreements) apply? |
| LQ-13 | P0 | Recourse vs. non-recourse: mandatory contract content for each; does the platform have any obligation regarding regress (Art. 16, 8-day notice)? |
| LQ-14 | P0 | Debtor notification: who must notify, in what form, and when? From the CEF start date, does CEF registration satisfy or replace notification? May the platform send notifications on behalf of the seller or factor? |
| LQ-15 | P1 | Reverse factoring (debtor-initiated): are there additional requirements if added later? |
| LQ-16 | P1 | Public-sector debtors: eligibility and interplay with the Central Invoice Register (CRF). |

## D. Central Factoring Register (CEF)
| ID | P | Question |
|----|---|----------|
| LQ-17 | P0 | Exact trigger for the assignor's registration duty ("immediately after recording in SEF, no later than the next business day"): what does this mean for invoices assigned weeks after issuance? |
| LQ-18 | P1 | May the platform register invoices in the CEF on behalf of users (API/authorised intermediary), given the high-assurance eID requirement? |
| LQ-19 | P1 | Legal effect of the CEF entry: priority between competing assignees, effect on debtor's discharge, effect on null second agreements. |
| LQ-20 | P0 | Before the CEF is live: what disclosure must the platform make to factors about the residual double-financing risk from off-platform transactions? |

## E. SEF / e-invoicing
| ID | P | Question |
|----|---|----------|
| LQ-21 | P0 | Is it permitted under SEF terms of use for a seller to give its SEF API key to a third-party platform for read access? Any formal authorisation (*ovlašćenje*) required? |
| LQ-22 | P1 | Does SEF have (or plan) a factoring/assignment flag? Confirm the rule that a factored invoice cannot be cancelled or storno'd, and its source article. |
| LQ-23 | P0 | Is a SEF invoice with buyer status "approved" legally an acknowledgement of the debt by the debtor? (This is used as an eligibility rule.) |
| LQ-51 | P0 | The SEF API key has no read-only scope (verified in the demo Swagger 2026-10-13): the key a seller gives the platform also allows cancelling, storno-ing and deleting invoices. What liability and security obligations follow for the platform holding such a key, and what must the seller's authorisation (LQ-21) and our terms say about it? |

## F. Contracts & e-signatures
| ID | P | Question |
|----|---|----------|
| LQ-24 | P0 | Required signature level for the factoring agreement and for seller declarations: qualified electronic signature (QES), advanced, or a simple click-accept? |
| LQ-25 | P0 | Can the platform's online offer acceptance be a binding pre-contract, or only the signed factoring agreement? When does ownership of the receivable transfer (signing, payment, notification)? |
| LQ-26 | P0 | Platform Terms of Use: are click-wrap terms enforceable against legal entities? Required content (liability limitation, confidentiality, governing law, dispute forum). Includes the clause that the company is liable for all actions taken through its shared account (ADR-020). |
| LQ-27 | P1 | May the platform provide a standard factoring agreement template, or must each factor use its own? Liability for template content. |

## G. KYC / AML / sanctions
| ID | P | Question |
|----|---|----------|
| LQ-28 | P0 | Is the platform an AML obligor under any category of Art. 4? |
| LQ-29 | P0 | May factors rely on platform-collected KYB data (third-party reliance)? If not, what is the lawful basis and the consent form for sharing the KYB package? |
| LQ-30 | P0 | Minimum KYB the platform must or should perform: legal representative identification, beneficial owners (APR register), PEP checks. |
| LQ-31 | P1 | Sanctions lists to screen: Serbian/UN designations; are EU and OFAC lists expected by partner banks or factors? |
| LQ-32 | P1 | Remote identification of legal representatives: qualified certificate, video identification (who may use it), or eID.gov.rs high-assurance login? Also: which identifiers (company name, PIB, MB, personal ID number) each Serbian QES issuer (Pošta, Halcom, E-Smart, MUP ID card) includes in its certificates, and which certificate types prove a link to the legal entity. Is a scanned hand-signed pristupnica, confirmed through the company's official email in APR, acceptable for onboarding (ADR-020)? |
| LQ-52 | P2 | May a private platform use **eID.gov.rs** as a login or identification provider (relying party) for its users? Under what conditions and contract (Office for IT and eGovernment)? Related to LQ-32 (and LQ-18 for CEF access). |
| LQ-33 | P1 | Platform duties when it detects suspicious activity (it is not an obligor): may or must it inform factors or authorities? Tipping-off risks. |

## H. Data protection & confidentiality
| ID | P | Question |
|----|---|----------|
| LQ-34 | P0 | Controller/processor roles: platform vs. factors for seller representatives' and debtor contacts' personal data. |
| LQ-35 | P0 | Data transfer to Azure EU regions under ZZPL (adequacy, need for SCCs). Any data-localisation requirement for financial records? |
| LQ-36 | P0 | Can debtor identity and invoice details be shown to many factors (trade secrets, confidentiality clauses in the seller-debtor contract)? Is the seller's consent sufficient? |
| LQ-37 | P1 | Need for a DPO, a DPIA, and a records-of-processing register. |
| LQ-38 | P1 | Logging IP and device data for audit and fraud: lawful basis and retention period. |

## I. Liability, warranties, disputes, fraud
| ID | P | Question |
|----|---|----------|
| LQ-39 | P0 | Platform liability if a verified invoice proves fraudulent, or a factor's approval is revoked mid-deal. Possible limitation and its enforceability. |
| LQ-40 | P0 | Seller warranties to include in the platform terms vs. in the factoring agreement (existence, no prior assignment, no set-off, no prohibition on assignment). |
| LQ-41 | P1 | Dispute resolution: platform role (none / facilitation / mediation), governing law, court jurisdiction or arbitration. |
| LQ-42 | P1 | Obligations if the platform suspects fraud or double financing: freezing a listing, informing the factor, reporting. |

## J. Tax & accounting *(tax)*
| ID | P | Question |
|----|---|----------|
| LQ-43 | P0 | Is the platform fee subject to 20% VAT, or exempt as intermediation in financial services (VAT Law Art. 25)? Does the answer differ if the seller or the factor pays? |
| LQ-44 | P0 | The platform must issue its fee invoices via SEF (B2B, VAT). Confirm the timing of the tax point (on funding confirmation?). |
| LQ-45 | P1 | Revenue recognition for success fees; treatment of cancelled or reversed deals (credit notes). |
| LQ-46 | P1 | Withholding tax or VAT reverse charge if factors or sellers are foreign (future). |

## K. Retention & cross-border
| ID | P | Question |
|----|---|----------|
| LQ-47 | P0 | Retention periods per record class: deal and contract records, KYB documents, audit logs, invoices, platform fee invoices (accounting law, e-invoicing law, AML). |
| LQ-48 | P1 | Right to erasure vs. mandatory retention: handling account deletion requests. |
| LQ-49 | P2 | Cross-border: foreign sellers, foreign debtors (export invoices outside SEF), EUR-denominated receivables, and the foreign-exchange law. |
| LQ-50 | P1 | EUR-indexed invoices payable in RSD: how the assigned amount and the purchase price are expressed legally. |
