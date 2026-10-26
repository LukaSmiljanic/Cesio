// What each notification type looks like: label, icon, colour and group (lists, preferences and toasts).
export type Tone = 'accent' | 'pos' | 'warn' | 'crit' | 'neutral'

/** Icon paths (24×24, stroked) per kind of notification. */
export const icons = {
  deal: 'M8 7V5a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2M4 7h16a1 1 0 0 1 1 1v10a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V8a1 1 0 0 1 1-1zM3 13h18',
  money: 'M3 7h18v10H3zM12 15a3 3 0 1 0 0-6 3 3 0 0 0 0 6zM6 10v4M18 10v4',
  offer:
    'M7 7h.01M3 11.6 11.6 3H20a1 1 0 0 1 1 1v8.4L12.4 21a1.4 1.4 0 0 1-2 0L3 13.6a1.4 1.4 0 0 1 0-2z',
  document:
    'M14 3v4a1 1 0 0 0 1 1h4M17 21H7a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h7l5 5v11a2 2 0 0 1-2 2zM9 13h6M9 17h6',
  shield: 'M12 3l8 4v5c0 5-3.5 8.5-8 9-4.5-.5-8-4-8-9V7l8-4zM9 12l2 2 4-4',
  lock: 'M6 11h12v9H6zM8 11V7a4 4 0 0 1 8 0v4',
  clock: 'M12 21a9 9 0 1 0 0-18 9 9 0 0 0 0 18zM12 7v5l3 3',
  bell: 'M10 5a2 2 0 1 1 4 0 7 7 0 0 1 4 6v3a4 4 0 0 0 2 3H4a4 4 0 0 0 2-3v-3a7 7 0 0 1 4-6M9 17v1a3 3 0 0 0 6 0v-1',
}

const kinds: Record<
  string,
  { label: string; icon: keyof typeof icons; tone: Tone; group: string }
> = {
  deal_action_required: {
    label: 'Potrebna je vaša radnja na poslu',
    icon: 'deal',
    tone: 'warn',
    group: 'Poslovi',
  },
  funding_reported: {
    label: 'Faktor je prijavio isplatu',
    icon: 'money',
    tone: 'pos',
    group: 'Poslovi',
  },
  funding_confirmed: {
    label: 'Isplata je potvrđena',
    icon: 'money',
    tone: 'pos',
    group: 'Poslovi',
  },
  deal_cancelled: { label: 'Posao je otkazan', icon: 'deal', tone: 'crit', group: 'Poslovi' },
  offer_received: { label: 'Nova ponuda', icon: 'offer', tone: 'accent', group: 'Ponude i oglasi' },
  offer_withdrawn: {
    label: 'Ponuda je povučena',
    icon: 'offer',
    tone: 'neutral',
    group: 'Ponude i oglasi',
  },
  offer_accepted: {
    label: 'Vaša ponuda je prihvaćena',
    icon: 'offer',
    tone: 'pos',
    group: 'Ponude i oglasi',
  },
  offer_superseded: {
    label: 'Vaša ponuda više nije aktivna',
    icon: 'offer',
    tone: 'neutral',
    group: 'Ponude i oglasi',
  },
  new_listing: {
    label: 'Novi oglas po vašim filterima',
    icon: 'offer',
    tone: 'accent',
    group: 'Ponude i oglasi',
  },
  receivable_verification: {
    label: 'Provera potraživanja je završena',
    icon: 'document',
    tone: 'neutral',
    group: 'Potraživanja',
  },
  sef_status_changed: {
    label: 'SEF status fakture se promenio',
    icon: 'document',
    tone: 'warn',
    group: 'Potraživanja',
  },
  kyb_decision: {
    label: 'Odluka o proveri firme',
    icon: 'shield',
    tone: 'accent',
    group: 'Firma i provere',
  },
  factor_authorization: {
    label: 'Promena ovlašćenja za faktoring',
    icon: 'shield',
    tone: 'warn',
    group: 'Firma i provere',
  },
  hold: { label: 'Promena blokade', icon: 'shield', tone: 'warn', group: 'Firma i provere' },
  fee_statement: {
    label: 'Izdat je obračun naknade',
    icon: 'money',
    tone: 'neutral',
    group: 'Naknade',
  },
  security: { label: 'Bezbednosno obaveštenje', icon: 'lock', tone: 'crit', group: 'Bezbednost' },
  reminder: { label: 'Podsetnik', icon: 'clock', tone: 'warn', group: 'Ostalo' },
}

const fallback = {
  label: 'Obaveštenje',
  icon: 'bell' as const,
  tone: 'neutral' as Tone,
  group: 'Ostalo',
}
export const kindOf = (type: string) => kinds[type] ?? { ...fallback, label: type }

export const toneClass: Record<Tone, string> = {
  accent: 'bg-accent-soft text-accent',
  pos: 'bg-pos-soft text-pos',
  warn: 'bg-warn-soft text-warn',
  crit: 'bg-crit-soft text-crit',
  neutral: 'bg-surface-2 text-ink-2',
}
