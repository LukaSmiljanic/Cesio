// Form controls in the look of the design proposals: option cards instead of radio buttons, a segmented choice for
// short options, a drop zone for attachments, a checklist with progress and a horizontal stepper.
import { useId, useRef, useState, type DragEvent, type ReactNode } from 'react'

export interface Option<T extends string> {
  value: T
  title: string
  description?: ReactNode
}

/** A choice as cards (a radio group underneath, so the keyboard and screen readers work as usual). */
export function OptionCards<T extends string>({
  label,
  value,
  options,
  onChange,
}: {
  label: string
  value: T
  options: Option<T>[]
  onChange: (value: T) => void
}) {
  const name = useId()
  return (
    <fieldset className="flex min-w-0 flex-col gap-1.5">
      <legend className="mb-1.5 text-[12.5px] font-semibold text-ink-2">{label}</legend>
      <div className="grid grid-cols-[repeat(auto-fit,minmax(200px,1fr))] gap-2.5">
        {options.map((option) => {
          const checked = option.value === value
          return (
            <label
              key={option.value}
              className={`flex cursor-pointer items-start gap-3 rounded-lg border px-4 py-3.5 has-[:focus-visible]:outline-2 has-[:focus-visible]:outline-focus ${
                checked
                  ? 'border-accent bg-accent-soft'
                  : 'border-line bg-surface hover:bg-surface-2'
              }`}
            >
              <input
                type="radio"
                name={name}
                className="sr-only"
                checked={checked}
                onChange={() => onChange(option.value)}
              />
              <span
                aria-hidden
                className={`mt-0.5 grid size-[18px] flex-none place-items-center rounded-full border-[1.5px] ${
                  checked ? 'border-accent' : 'border-ink-3'
                }`}
              >
                {checked && <span className="size-2 rounded-full bg-accent" />}
              </span>
              <span>
                <span className="block font-semibold">{option.title}</span>
                {option.description && (
                  <span className="block text-[13px] text-ink-2">{option.description}</span>
                )}
              </span>
            </label>
          )
        })}
      </div>
    </fieldset>
  )
}

/** Two to four short options side by side. */
export function Segmented<T extends string>({
  label,
  value,
  options,
  onChange,
}: {
  label: string
  value: T
  options: { value: T; label: string }[]
  onChange: (value: T) => void
}) {
  return (
    <div className="flex min-w-0 flex-col gap-1.5">
      <span className="text-[12.5px] font-semibold text-ink-2">{label}</span>
      <div
        role="group"
        aria-label={label}
        className="inline-flex w-fit flex-wrap gap-[3px] rounded-lg border border-line bg-surface-2 p-[3px]"
      >
        {options.map((option) => (
          <button
            key={option.value}
            type="button"
            aria-pressed={option.value === value}
            onClick={() => onChange(option.value)}
            className={`rounded-md px-3 py-1.5 text-sm font-medium ${
              option.value === value
                ? 'bg-surface text-ink shadow-panel'
                : 'text-ink-2 hover:text-ink'
            }`}
          >
            {option.label}
          </button>
        ))}
      </div>
    </div>
  )
}

type FileStatus = 'busy' | 'done' | 'selected' | 'failed'

interface PickedFile {
  key: number
  name: string
  size: number
  status: FileStatus
}

const statusLook: Record<FileStatus, [string, string]> = {
  busy: ['Otpremanje…', 'bg-warn-soft text-warn'],
  done: ['Priloženo', 'bg-pos-soft text-pos'],
  selected: ['Izabrano', 'bg-accent-soft text-accent'],
  failed: ['Odbijeno', 'bg-crit-soft text-crit'],
}

function size(bytes: number) {
  return bytes < 1024 * 1024
    ? `${Math.max(1, Math.round(bytes / 1024))} KB`
    : `${(bytes / (1024 * 1024)).toFixed(1).replace('.', ',')} MB`
}

/**
 * A drop zone for one file at a time, with the files picked so far and their status. `onFile` uploads (returns a
 * promise) or only selects the file for a later submit (returns nothing).
 */
export function FileDrop({
  label,
  hint,
  accept,
  onFile,
}: {
  label: string
  hint?: string
  accept?: string
  onFile: (file: File) => Promise<unknown> | void
}) {
  const id = useId()
  const counter = useRef(0)
  const [over, setOver] = useState(false)
  const [files, setFiles] = useState<PickedFile[]>([])

  function update(key: number, status: FileStatus) {
    setFiles((list) => list.map((f) => (f.key === key ? { ...f, status } : f)))
  }

  function pick(file: File | undefined) {
    if (!file) {
      return
    }
    const key = ++counter.current
    const result = onFile(file)
    setFiles((list) => [
      ...list.filter((f) => f.status !== 'selected'),
      { key, name: file.name, size: file.size, status: result ? 'busy' : 'selected' },
    ])
    if (result) {
      result.then(
        () => update(key, 'done'),
        () => update(key, 'failed'),
      )
    }
  }

  function drop(event: DragEvent<HTMLLabelElement>) {
    event.preventDefault()
    setOver(false)
    pick(event.dataTransfer.files[0])
  }

  return (
    <div className="flex min-w-0 flex-col gap-2">
      <span className="text-[12.5px] font-semibold text-ink-2">{label}</span>
      <label
        htmlFor={id}
        onDragOver={(e) => (e.preventDefault(), setOver(true))}
        onDragLeave={() => setOver(false)}
        onDrop={drop}
        className={`flex cursor-pointer flex-col items-center gap-1 rounded-xl border-[1.5px] border-dashed px-4 py-5 text-center has-[:focus-visible]:outline-2 has-[:focus-visible]:outline-focus ${
          over ? 'border-accent bg-accent-soft' : 'border-ink-3 bg-surface-2 hover:border-accent'
        }`}
      >
        <svg
          aria-hidden
          viewBox="0 0 24 24"
          className="size-6 text-ink-2"
          fill="none"
          stroke="currentColor"
          strokeWidth="1.8"
          strokeLinecap="round"
          strokeLinejoin="round"
        >
          <path d="M12 16V4m0 0-4 4m4-4 4 4M4 16v2a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2v-2" />
        </svg>
        <span className="font-semibold">Prevucite fajl ovde</span>
        <span className="text-[13px] text-ink-2">
          ili <span className="text-accent underline">izaberite sa računara</span>
          {hint && ` · ${hint}`}
        </span>
        <input
          id={id}
          type="file"
          accept={accept}
          aria-label={label}
          className="sr-only"
          onChange={(e) => {
            pick(e.target.files?.[0])
            e.target.value = ''
          }}
        />
      </label>
      {files.length > 0 && (
        <ul className="overflow-hidden rounded-lg border border-line bg-surface">
          {files.map((f) => (
            <li
              key={f.key}
              className="flex items-center gap-3 border-b border-line px-3.5 py-2.5 last:border-b-0"
            >
              <svg
                aria-hidden
                viewBox="0 0 24 24"
                className="size-5 flex-none text-ink-2"
                fill="none"
                stroke="currentColor"
                strokeWidth="1.8"
                strokeLinecap="round"
                strokeLinejoin="round"
              >
                <path d="M14 3v4a1 1 0 0 0 1 1h4M17 21H7a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h7l5 5v11a2 2 0 0 1-2 2zM9 13h6M9 17h6" />
              </svg>
              <span className="min-w-0 flex-1">
                <span className="block truncate text-sm font-semibold">{f.name}</span>
                <span className="block text-xs text-ink-3">{size(f.size)}</span>
              </span>
              <span
                className={`rounded-full px-2.5 py-0.5 text-xs font-semibold whitespace-nowrap ${statusLook[f.status][1]}`}
              >
                {statusLook[f.status][0]}
              </span>
            </li>
          ))}
        </ul>
      )}
    </div>
  )
}

/** Statements to confirm, with how many are confirmed. */
export function Checklist({
  title,
  items,
  values,
  onChange,
}: {
  title: string
  items: { key: string; label: string }[]
  values: Record<string, boolean>
  onChange: (key: string, checked: boolean) => void
}) {
  const confirmed = items.filter((item) => values[item.key]).length
  return (
    <fieldset className="rounded-lg border border-line bg-surface px-4 py-1.5">
      <legend className="sr-only">{title}</legend>
      <div className="flex items-center justify-between border-b border-line py-2">
        <span className="font-semibold">{title}</span>
        <span className="text-[13px] text-ink-2">
          {confirmed} od {items.length} potvrđeno
        </span>
      </div>
      {items.map((item) => (
        <label key={item.key} className="flex cursor-pointer items-start gap-2.5 py-2.5 text-sm">
          <input
            type="checkbox"
            className="mt-0.5 size-4 flex-none accent-[var(--accent)]"
            checked={values[item.key] ?? false}
            onChange={(e) => onChange(item.key, e.target.checked)}
          />
          <span className={values[item.key] ? '' : 'text-ink-2'}>{item.label}</span>
        </label>
      ))}
    </fieldset>
  )
}

/** Steps in a row: done ones ticked, the first open one current. */
export function Stepper({ steps }: { steps: { label: string; done: boolean }[] }) {
  const current = steps.findIndex((s) => !s.done)
  return (
    <ol className="flex flex-wrap items-center gap-x-3 gap-y-2">
      {steps.map((step, index) => (
        <li key={step.label} className="flex items-center gap-3">
          <span
            className={`flex items-center gap-2 text-[13px] whitespace-nowrap ${
              index === current ? 'font-semibold text-ink' : step.done ? 'text-ink' : 'text-ink-3'
            }`}
            aria-current={index === current ? 'step' : undefined}
          >
            <span
              aria-hidden
              className={`grid size-[22px] place-items-center rounded-full border-[1.5px] text-xs font-semibold ${
                step.done
                  ? 'border-pos bg-pos text-surface'
                  : index === current
                    ? 'border-accent text-accent'
                    : 'border-line text-ink-3'
              }`}
            >
              {step.done ? '✓' : index + 1}
            </span>
            {step.label}
            {step.done && <span className="sr-only">(završeno)</span>}
          </span>
          {index < steps.length - 1 && <span aria-hidden className="h-px w-5 bg-line" />}
        </li>
      ))}
    </ol>
  )
}
