// Components in the look of design/prototype.html (ADR-010): Tailwind classes on the theme tokens, no extra runtime
// dependency.
import { useContext } from 'react'
import type { ButtonHTMLAttributes, InputHTMLAttributes, ReactNode } from 'react'
import { ApiError } from '../api/client'
import { buttonClass, InShell } from './styles'

export function Button({
  className = '',
  variant = 'primary',
  size = 'md',
  ...props
}: ButtonHTMLAttributes<HTMLButtonElement> & {
  variant?: 'primary' | 'secondary'
  size?: 'md' | 'sm'
}) {
  return <button className={`${buttonClass(variant, size)} ${className}`} {...props} />
}

/** A table inside a panel, with an empty-state row. */
export function EmptyRow({ columns, children }: { columns: number; children: ReactNode }) {
  return (
    <tr>
      <td colSpan={columns} className="py-6 text-ink-2">
        {children}
      </td>
    </tr>
  )
}

export function Field({
  label,
  id,
  ...props
}: InputHTMLAttributes<HTMLInputElement> & { label: string; id: string }) {
  return (
    <div className="flex min-w-0 flex-col gap-1.5">
      <label htmlFor={id} className="text-[12.5px] font-semibold text-ink-2">
        {label}
      </label>
      <input
        id={id}
        className="h-9 w-full rounded-[7px] border border-line bg-surface px-2.5 text-sm focus:border-accent focus:outline-2 focus:outline-accent-soft"
        {...props}
      />
    </div>
  )
}

export function Card({ title, children }: { title: string; children: ReactNode }) {
  if (useContext(InShell)) {
    return (
      <section className="flex flex-col gap-5">
        <h1 className="text-[28px] leading-tight">{title}</h1>
        <div className="rounded-lg border border-line bg-surface p-5">{children}</div>
      </section>
    )
  }
  return (
    <section className="mx-auto mt-16 w-full max-w-md rounded-xl border border-line bg-surface p-7 shadow-panel">
      <div className="mb-5 flex items-center gap-2.5">
        <BrandMark />
        <h1 className="text-[22px]">{title}</h1>
      </div>
      {children}
    </section>
  )
}

export function BrandMark() {
  return (
    <span className="grid size-[26px] place-items-center rounded-md bg-accent font-mono text-[13px] font-medium text-accent-ink">
      C
    </span>
  )
}

/** A panel with an optional header row (title left, actions right), as in the prototype. */
export function Panel({
  title,
  actions,
  children,
  padded = true,
}: {
  title?: ReactNode
  actions?: ReactNode
  children: ReactNode
  padded?: boolean
}) {
  return (
    <section className="overflow-hidden rounded-lg border border-line bg-surface">
      {(title || actions) && (
        <div className="flex flex-wrap items-center justify-between gap-3 border-b border-line px-[18px] py-3.5">
          {title && <h2 className="text-[15px] font-semibold">{title}</h2>}
          {actions && <div className="flex flex-wrap gap-2">{actions}</div>}
        </div>
      )}
      <div className={padded ? 'p-[18px]' : 'overflow-x-auto'}>{children}</div>
    </section>
  )
}

export function PageHeader({
  eyebrow,
  title,
  actions,
}: {
  eyebrow?: string
  title: string
  actions?: ReactNode
}) {
  return (
    <div className="flex flex-wrap items-end justify-between gap-x-6 gap-y-3">
      <div>
        {eyebrow && (
          <div className="text-[11.5px] font-semibold tracking-[0.08em] text-ink-3 uppercase">
            {eyebrow}
          </div>
        )}
        <h1 className="mt-1 text-[28px] leading-tight">{title}</h1>
      </div>
      {actions && <div className="flex flex-wrap gap-2">{actions}</div>}
    </div>
  )
}

/** KPI tiles separated by hairlines. */
export function Kpis({ items }: { items: { label: string; value: ReactNode; sub?: ReactNode }[] }) {
  return (
    <div className="grid grid-cols-1 gap-px sm:grid-cols-2 xl:grid-cols-4 overflow-hidden rounded-lg border border-line bg-line">
      {items.map((k) => (
        <div key={k.label} className="bg-surface px-[18px] py-4">
          <div className="text-[12.5px] text-ink-2">{k.label}</div>
          <div className="mt-1 font-display text-2xl font-medium tabular-nums">{k.value}</div>
          {k.sub && <div className="text-[12.5px] text-ink-2">{k.sub}</div>}
        </div>
      ))}
    </div>
  )
}

type Tone = 'neutral' | 'accent' | 'pos' | 'warn' | 'crit'

const tones: Record<Tone, string> = {
  neutral: 'bg-surface-2 text-ink-2',
  accent: 'bg-accent-soft text-accent',
  pos: 'bg-pos-soft text-pos',
  warn: 'bg-warn-soft text-warn',
  crit: 'bg-crit-soft text-crit',
}

export function Chip({ tone = 'neutral', children }: { tone?: Tone; children: ReactNode }) {
  return (
    <span
      className={`inline-flex items-center gap-1.5 rounded-full px-2.5 py-0.5 text-xs font-semibold whitespace-nowrap ${tones[tone]}`}
    >
      <span aria-hidden className="size-1.5 rounded-full bg-current" />
      {children}
    </span>
  )
}

/** Serbian labels and colours of the states (ux-design.md §5): colour encodes state, never decoration. */
const states: Record<string, [string, Tone]> = {
  Draft: ['Nacrt', 'neutral'],
  PendingVerification: ['Na proveri', 'neutral'],
  Verified: ['Verifikovano', 'neutral'],
  Listed: ['Na tržištu', 'accent'],
  Open: ['Otvoren', 'accent'],
  Reserved: ['Rezervisano', 'accent'],
  Active: ['Aktivna', 'accent'],
  Accepted: ['Prihvaćena', 'pos'],
  Assigned: ['Ustupljeno', 'accent'],
  AwaitingSignatures: ['Čeka potpise', 'warn'],
  Signed: ['Potpisano', 'accent'],
  FundingReported: ['Isplata prijavljena', 'warn'],
  Funded: ['Finansirano', 'pos'],
  Settled: ['Naplaćeno', 'pos'],
  Closed: ['Zatvoreno', 'pos'],
  Paid: ['Plaćeno', 'pos'],
  Due: ['Dospeva', 'warn'],
  Invoiced: ['Fakturisano', 'neutral'],
  Rejected: ['Odbijeno', 'crit'],
  Cancelled: ['Otkazano', 'neutral'],
  Withdrawn: ['Povučeno', 'neutral'],
  Expired: ['Isteklo', 'neutral'],
  Ineligible: ['Ne ispunjava uslove', 'crit'],
  Defaulted: ['Neplaćeno', 'crit'],
  RecourseReturned: ['Vraćeno (regres)', 'warn'],
  Terminated: ['Raskinuto', 'crit'],
  Approved: ['Odobreno', 'pos'],
  Pending: ['Na čekanju', 'warn'],
  Suspended: ['Suspendovano', 'crit'],
}

export function StateChip({ state }: { state: string }) {
  const [label, tone] = states[state] ?? [state, 'neutral']
  return <Chip tone={tone}>{label}</Chip>
}

const messages: Record<string, string> = {
  invalid_credentials: 'Pogrešan email ili lozinka, ili je nalog privremeno zaključan.',
  invalid_code: 'Kod nije ispravan ili je istekao.',
  invalid_token: 'Link nije ispravan ili je istekao.',
  email_not_confirmed: 'Prvo potvrdite email adresu.',
  validation_failed: 'Podaci nisu ispravni.',
  PasswordBreached: 'Ova lozinka se pojavljuje u poznatim curenjima podataka. Izaberite drugu.',
  PasswordTooShort: 'Lozinka mora imati najmanje 12 znakova.',
  invalid_pib: 'PIB nije ispravan (9 cifara sa kontrolnom cifrom).',
  invalid_mb: 'Matični broj nije ispravan (8 cifara sa kontrolnom cifrom).',
  member_limit_reached: 'Firma ne može imati više članova.',
  signatory_requires_representative:
    'Potpisnik mora biti povezan sa zakonskim zastupnikom ili punomoćjem.',
  signatory_via_pristupnica: 'Nalog prodavca postaje potpisnik kada se prihvati pristupnica.',
  last_owner: 'Poslednji vlasnik ne može biti uklonjen.',
  organization_incomplete:
    'Nedostaju podaci: poslovno ime, matični broj, zastupnik ili prihvaćeni Uslovi.',
  concurrency_conflict: 'Podatke je u međuvremenu promenio neko drugi. Osvežite stranicu.',
  conflict_state: 'Ova radnja nije moguća u trenutnom statusu.',
  already_member: 'Već ste član ove firme.',
  not_found: 'Nije pronađeno.',
  precondition_required: 'Osvežite stranicu i pokušajte ponovo.',
  power_of_attorney_unverified:
    'Punomoćje će moći da se koristi kada provera dokumenata bude dostupna.',
  already_exists: 'Firma ili članstvo već postoje.',
  sef_connection_invalid: 'Prvo povežite važeći SEF API ključ.',
  sef_key_rejected: 'SEF je odbio API ključ.',
  sef_pib_mismatch: 'SEF ključ pripada firmi sa drugim PIB-om.',
  sef_no_invoices:
    'Za ovaj ključ SEF nema izlaznih faktura u poslednjih 90 dana, pa PIB ne može da se potvrdi.',
  upstream_unavailable: 'SEF trenutno nije dostupan. Pokušajte kasnije.',
  declaration_incomplete: 'Potvrdite sve stavke izjave pre slanja.',
  documents_missing: 'Priložite bar jedan prateći dokument (ugovor, porudžbenicu ili otpremnicu).',
  hold_active: 'Potraživanje je blokirano.',
  sef_not_approved: 'SEF trenutno ne prikazuje fakturu kao odobrenu od kupca.',
  wrong_document_purpose: 'Dokument nije otpremljen kao prateći dokument.',
  receivable_not_verified: 'Samo provereno potraživanje može da se objavi.',
  listing_already_open: 'Potraživanje već ima aktivan listing.',
  maturity_too_close: 'Rok za ponude mora biti dovoljno pre dospeća.',
  organization_not_approved: 'Objavljivanje zahteva odobrenu firmu bez blokade.',
  unknown_factor: 'Jedan od navedenih ID-jeva nije faktor.',
  factor_not_authorized: 'Marketplace je dostupan samo odobrenim i ovlašćenim faktorima.',
  listing_closed: 'Rok za ponude je prošao.',
  offer_exists: 'Već imate aktivnu ponudu na ovom listingu; prvo je povucite.',
  offer_not_active: 'Ponuda više nije aktivna.',
  listing_not_open: 'Listing više nije otvoren.',
  receivable_ineligible: 'Potraživanje više ne ispunjava uslove (SEF status se promenio).',
  signatory_required: 'Ovaj korak može da uradi samo potpisnik firme.',
  contract_changed: 'Ugovor je u međuvremenu zamenjen; potvrdite trenutnu verziju.',
  already_confirmed: 'Vaša firma je već potvrdila ovaj ugovor.',
  wrong_document: 'Otpremite ugovor kao PDF.',
  kyb_consent_missing: 'Prodavac još nije dao saglasnost za deljenje KYB paketa.',
  deadline_passed: 'Rok za potpise je istekao.',
  signature_invalid: 'Potpis nije ispravan ili ga nema.',
  certificate_not_qualified: 'Sertifikat nije kvalifikovan.',
  certificate_untrusted: 'Sertifikat nije od pouzdanog izdavaoca.',
  certificate_revoked: 'Sertifikat je opozvan.',
  document_mismatch: 'Potpisan je drugi dokument od usaglašenog ugovora.',
  signer_not_authorized: 'Potpisnik nije zastupnik nijedne strane.',
  account_not_usable: 'Uplata mora ići na verifikovan račun prodavca.',
  cef_not_registered: 'Isplata čeka upis ugovora u CEF.',
  evidence_missing: 'Priložite ispravan dokaz (dokument vaše firme).',
  staff_account: 'Nalog osoblja platforme ne može biti član firme.',
  screening_unresolved: 'Prvo razrešite sva moguća poklapanja sa sankcionim listama.',
  confirmed_match: 'Potvrđeno poklapanje sa sankcionom listom: firma se mora odbiti.',
  kyb_incomplete: 'Postavite rizik i potvrdite zastupnike pre odobrenja.',
  four_eyes_required: 'Potrebno je odobrenje drugog službenika.',
  charge_reversed: 'Naknada je stornirana.',
  pib_already_claimed:
    'Ova firma je već registrovana na platformi Cesio. Za pristup se obratite njenom administratoru.',
  official_email_missing:
    'Službeni email firme iz APR-a još nije upisan; obratite se podršci Cesio.',
  onboarding_incomplete: 'Prodavac mora imati potvrđen PIB (SEF) i prihvaćenu pristupnicu.',
  email_confirmation_missing: 'Skenirana pristupnica čeka potvrdu preko službenog emaila.',
  signer_required: 'Upišite potpisnika i zastupnika ili punomoćje.',
  confirmation_gone: 'Link više ne važi. Zatražite novi.',
  reset_already_requested: 'Reset je već zatražen za ovaj nalog.',
  organization_required: 'Član ste više firmi; obratite se podršci Cesio uz PIB firme.',
  invalid_method: 'Izaberite način potpisa.',
  authority_inconsistent: 'Izdavalac ne odgovara vrsti ovlašćenja.',
  decision_required: 'Društvo za faktoring navodi broj i datum rešenja i prilaže rešenje.',
  decision_not_applicable: 'Banka nema rešenje o faktoringu; ostavite ta polja prazna.',
  invalid_factor_organization: 'Unesite ispravan PIB, poslovno ime i email prvog korisnika.',
  unsupported_file_type: 'Dozvoljeni su PDF i slike.',
  invalid_account_number: 'Broj računa nije ispravan (šifra banke, broj računa i kontrolni broj).',
  unsupported_currency: 'Računi za isplatu su u dinarima (RSD).',
  duplicate_account: 'Isti račun je prijavila druga firma; prvo razrešite upozorenje.',
  reason_required: 'Unesite razlog.',
  notification_mandatory: 'Ovo obaveštenje ne može da se isključi.',
  authorization_pending: 'Već postoji ovlašćenje koje čeka proveru.',
}

export function ErrorMessage({ error }: { error: unknown }) {
  if (!error) {
    return null
  }
  const lines =
    error instanceof ApiError
      ? [
          messages[error.errorCode ?? ''] ?? error.message,
          ...error.errors.map((e) => messages[e.code] ?? e.message),
        ]
      : ['Došlo je do greške. Pokušajte ponovo.']
  return (
    <div role="alert" className="rounded-md bg-crit-soft px-3 py-2 text-[13px] text-crit">
      {[...new Set(lines)].map((line) => (
        <p key={line}>{line}</p>
      ))}
    </div>
  )
}
