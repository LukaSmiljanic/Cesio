import { icons, kindOf, toneClass } from '../notifications/kinds'

/** The round icon of a notification type. */
export function KindIcon({ type }: { type: string }) {
  const kind = kindOf(type)
  return (
    <span
      aria-hidden
      className={`grid size-9 flex-none place-items-center rounded-full ${toneClass[kind.tone]}`}
    >
      <svg
        viewBox="0 0 24 24"
        className="size-[18px]"
        fill="none"
        stroke="currentColor"
        strokeWidth="1.8"
        strokeLinecap="round"
        strokeLinejoin="round"
      >
        <path d={icons[kind.icon]} />
      </svg>
    </span>
  )
}
