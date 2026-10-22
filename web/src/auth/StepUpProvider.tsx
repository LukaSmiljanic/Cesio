import { useCallback, useRef, useState, type FormEvent, type ReactNode } from 'react'
import { authApi } from '../api/auth'
import { ApiError } from '../api/client'
import { Button, ErrorMessage, Field } from '../components/ui'
import { StepUpContext, type WithStepUp } from './stepUpContext'

/** The step-up modal (security.md §1: re-authentication within 5 minutes for sensitive actions). */
export function StepUpProvider({ children }: { children: ReactNode }) {
  const [open, setOpen] = useState(false)
  const [code, setCode] = useState('')
  const [error, setError] = useState<unknown>(null)
  const pending = useRef<{ resolve: () => void; reject: (reason: unknown) => void } | null>(null)

  const withStepUp = useCallback<WithStepUp>(async (action) => {
    try {
      return await action()
    } catch (e) {
      if (!(e instanceof ApiError) || e.errorCode !== 'step_up_required') {
        throw e
      }
      await new Promise<void>((resolve, reject) => {
        pending.current = { resolve, reject }
        setCode('')
        setError(null)
        setOpen(true)
      })
      return action()
    }
  }, [])

  async function submit(event: FormEvent) {
    event.preventDefault()
    try {
      await authApi.stepUp(code)
      setOpen(false)
      pending.current?.resolve()
    } catch (e) {
      setError(e)
    }
  }

  function cancel() {
    setOpen(false)
    pending.current?.reject(new Error('Step-up cancelled'))
  }

  return (
    <StepUpContext.Provider value={withStepUp}>
      {children}
      {open && (
        <div
          className="fixed inset-0 flex items-center justify-center bg-slate-900/40"
          role="dialog"
          aria-modal="true"
          aria-labelledby="step-up-title"
        >
          <form
            onSubmit={submit}
            className="w-full max-w-sm space-y-4 rounded-lg bg-white p-6 shadow-lg"
          >
            <h2 id="step-up-title" className="text-lg font-semibold">
              Potvrdite identitet
            </h2>
            <p className="text-sm text-slate-600">Unesite kod iz aplikacije za autentifikaciju.</p>
            <Field
              id="step-up-code"
              label="Kod"
              inputMode="numeric"
              autoComplete="one-time-code"
              value={code}
              onChange={(e) => setCode(e.target.value)}
            />
            <ErrorMessage error={error} />
            <div className="flex justify-end gap-2">
              <Button type="button" variant="secondary" onClick={cancel}>
                Otkaži
              </Button>
              <Button type="submit">Potvrdi</Button>
            </div>
          </form>
        </div>
      )}
    </StepUpContext.Provider>
  )
}
