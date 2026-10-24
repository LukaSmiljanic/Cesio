// Minimal components in the shadcn/ui style (ADR-010): Tailwind classes, no extra runtime dependency.
import type { ButtonHTMLAttributes, InputHTMLAttributes, ReactNode } from 'react'
import { ApiError } from '../api/client'

export function Button({
  className = '',
  variant = 'primary',
  ...props
}: ButtonHTMLAttributes<HTMLButtonElement> & { variant?: 'primary' | 'secondary' }) {
  const look =
    variant === 'primary'
      ? 'bg-slate-900 text-white hover:bg-slate-700'
      : 'border border-slate-300 bg-white text-slate-900 hover:bg-slate-100'
  return (
    <button
      className={`inline-flex h-10 items-center justify-center rounded-md px-4 text-sm font-medium disabled:opacity-50 ${look} ${className}`}
      {...props}
    />
  )
}

export function Field({
  label,
  id,
  ...props
}: InputHTMLAttributes<HTMLInputElement> & { label: string; id: string }) {
  return (
    <div className="space-y-1">
      <label htmlFor={id} className="block text-sm font-medium">
        {label}
      </label>
      <input
        id={id}
        className="h-10 w-full rounded-md border border-slate-300 bg-white px-3 text-sm focus:outline-2 focus:outline-slate-400"
        {...props}
      />
    </div>
  )
}

export function Card({ title, children }: { title: string; children: ReactNode }) {
  return (
    <section className="mx-auto mt-16 w-full max-w-md rounded-lg border border-slate-200 bg-white p-6 shadow-sm">
      <h1 className="mb-4 text-xl font-semibold">{title}</h1>
      {children}
    </section>
  )
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
    <div
      role="alert"
      className="rounded-md border border-red-200 bg-red-50 p-3 text-sm text-red-800"
    >
      {[...new Set(lines)].map((line) => (
        <p key={line}>{line}</p>
      ))}
    </div>
  )
}
