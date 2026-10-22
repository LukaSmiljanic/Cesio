import { useMutation, useQueryClient } from '@tanstack/react-query'

import { useEffect, useState, type FormEvent } from 'react'
import { Link, useNavigate, useSearchParams } from 'react-router'
import { authApi } from '../api/auth'
import { ProvisioningView } from '../components/ProvisioningView'
import { Button, Card, ErrorMessage, Field } from '../components/ui'

const linkClass = 'text-sm text-slate-600 underline hover:text-slate-900'

export function LoginPage() {
  const navigate = useNavigate()
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const login = useMutation({
    mutationFn: () => authApi.login(email, password),
    onSuccess: ({ next }) => navigate(next === 'enrol' ? '/mfa/enrol' : '/mfa/verify'),
  })

  return (
    <Card title="Prijava">
      <form className="space-y-4" onSubmit={(e) => (e.preventDefault(), login.mutate())}>
        <Field
          id="email"
          label="Email"
          type="email"
          autoComplete="username"
          required
          value={email}
          onChange={(e) => setEmail(e.target.value)}
        />
        <Field
          id="password"
          label="Lozinka"
          type="password"
          autoComplete="current-password"
          required
          value={password}
          onChange={(e) => setPassword(e.target.value)}
        />
        <ErrorMessage error={login.error} />
        <Button type="submit" className="w-full" disabled={login.isPending}>
          Prijavi se
        </Button>
      </form>
      <div className="mt-4 flex justify-between">
        <Link className={linkClass} to="/register">
          Nemate nalog? Registracija
        </Link>
        <Link className={linkClass} to="/forgot-password">
          Zaboravljena lozinka
        </Link>
      </div>
    </Card>
  )
}

export function RegisterPage() {
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const register = useMutation({ mutationFn: () => authApi.register(email, password) })

  if (register.isSuccess) {
    return (
      <Card title="Proverite email">
        <p className="text-sm">
          Ako adresa još nije registrovana, poslali smo link za potvrdu na {email}.
        </p>
      </Card>
    )
  }

  return (
    <Card title="Registracija">
      <form className="space-y-4" onSubmit={(e) => (e.preventDefault(), register.mutate())}>
        <Field
          id="email"
          label="Email"
          type="email"
          autoComplete="username"
          required
          value={email}
          onChange={(e) => setEmail(e.target.value)}
        />
        <Field
          id="password"
          label="Lozinka (najmanje 12 znakova)"
          type="password"
          autoComplete="new-password"
          minLength={12}
          required
          value={password}
          onChange={(e) => setPassword(e.target.value)}
        />
        <ErrorMessage error={register.error} />
        <Button type="submit" className="w-full" disabled={register.isPending}>
          Registruj se
        </Button>
      </form>
      <Link className={`${linkClass} mt-4 block`} to="/login">
        Već imate nalog? Prijava
      </Link>
    </Card>
  )
}

/** Reads the one-time link values once and removes them from the address bar and history. */
function useLinkValues(...names: string[]) {
  const [params] = useSearchParams()
  const [values] = useState(() => names.map((name) => params.get(name) ?? ''))
  useEffect(() => window.history.replaceState(null, '', window.location.pathname), [])
  return values
}

export function ConfirmEmailPage() {
  const [userId, token] = useLinkValues('userId', 'token')
  const confirm = useMutation({ mutationFn: () => authApi.confirmEmail(userId, token) })
  const { mutate } = confirm

  useEffect(() => mutate(), [mutate])

  return (
    <Card title="Potvrda email adrese">
      {confirm.isPending && <p className="text-sm">Potvrđujemo...</p>}
      {confirm.isSuccess && (
        <p className="text-sm">
          Email je potvrđen.{' '}
          <Link className={linkClass} to="/login">
            Prijavite se
          </Link>
        </p>
      )}
      <ErrorMessage error={confirm.error} />
    </Card>
  )
}

export function MfaEnrolPage() {
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const [code, setCode] = useState('')
  const start = useMutation({ mutationFn: authApi.startEnrolment })
  const confirm = useMutation({ mutationFn: () => authApi.confirmEnrolment(code) })
  const { mutate: startEnrolment } = start

  useEffect(() => startEnrolment(), [startEnrolment])

  if (confirm.data) {
    return (
      <Card title="Kodovi za oporavak">
        <p className="mb-3 text-sm">
          Sačuvajte ove kodove na sigurnom mestu. Prikazuju se samo sada; svaki važi jednom.
        </p>
        <ul className="mb-4 grid grid-cols-2 gap-1 font-mono text-sm">
          {confirm.data.recoveryCodes.map((c) => (
            <li key={c}>{c}</li>
          ))}
        </ul>
        <Button
          className="w-full"
          onClick={async () => {
            await queryClient.invalidateQueries({ queryKey: ['me'] })
            navigate('/')
          }}
        >
          Sačuvao sam kodove
        </Button>
      </Card>
    )
  }

  return (
    <Card title="Podešavanje dvofaktorske prijave">
      <p className="mb-4 text-sm">
        Skenirajte QR kod aplikacijom za autentifikaciju (npr. Google ili Microsoft Authenticator) i
        unesite kod.
      </p>
      {start.data && <ProvisioningView provisioning={start.data} />}
      <ErrorMessage error={start.error} />
      <form className="mt-4 space-y-4" onSubmit={(e) => (e.preventDefault(), confirm.mutate())}>
        <Field
          id="code"
          label="Kod"
          inputMode="numeric"
          autoComplete="one-time-code"
          required
          value={code}
          onChange={(e) => setCode(e.target.value)}
        />
        <ErrorMessage error={confirm.error} />
        <Button type="submit" className="w-full" disabled={confirm.isPending}>
          Potvrdi
        </Button>
      </form>
    </Card>
  )
}

export function MfaVerifyPage() {
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const [value, setValue] = useState('')
  const [useRecovery, setUseRecovery] = useState(false)
  const verify = useMutation({
    mutationFn: () => authApi.verify(useRecovery ? null : value, useRecovery ? value : null),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ['me'] })
      navigate('/')
    },
  })

  return (
    <Card title="Dvofaktorska prijava">
      <form className="space-y-4" onSubmit={(e) => (e.preventDefault(), verify.mutate())}>
        <Field
          id="code"
          label={useRecovery ? 'Kod za oporavak' : 'Kod iz aplikacije'}
          autoComplete="one-time-code"
          required
          value={value}
          onChange={(e) => setValue(e.target.value)}
        />
        <ErrorMessage error={verify.error} />
        <Button type="submit" className="w-full" disabled={verify.isPending}>
          Potvrdi
        </Button>
      </form>
      <button
        type="button"
        className={`${linkClass} mt-4`}
        onClick={() => (setUseRecovery(!useRecovery), setValue(''))}
      >
        {useRecovery ? 'Koristi kod iz aplikacije' : 'Koristi kod za oporavak'}
      </button>
    </Card>
  )
}

export function ForgotPasswordPage() {
  const [email, setEmail] = useState('')
  const forgot = useMutation({ mutationFn: () => authApi.forgotPassword(email) })

  return (
    <Card title="Zaboravljena lozinka">
      {forgot.isSuccess ? (
        <p className="text-sm">Ako je adresa registrovana, poslali smo link za novu lozinku.</p>
      ) : (
        <form className="space-y-4" onSubmit={(e) => (e.preventDefault(), forgot.mutate())}>
          <Field
            id="email"
            label="Email"
            type="email"
            required
            value={email}
            onChange={(e) => setEmail(e.target.value)}
          />
          <ErrorMessage error={forgot.error} />
          <Button type="submit" className="w-full" disabled={forgot.isPending}>
            Pošalji link
          </Button>
        </form>
      )}
    </Card>
  )
}

export function ResetPasswordPage() {
  const [userId, token] = useLinkValues('userId', 'token')
  const [password, setPassword] = useState('')
  const reset = useMutation({ mutationFn: () => authApi.resetPassword(userId, token, password) })

  function submit(event: FormEvent) {
    event.preventDefault()
    reset.mutate()
  }

  return (
    <Card title="Nova lozinka">
      {reset.isSuccess ? (
        <p className="text-sm">
          Lozinka je promenjena i sve sesije su odjavljene.{' '}
          <Link className={linkClass} to="/login">
            Prijavite se
          </Link>
        </p>
      ) : (
        <form className="space-y-4" onSubmit={submit}>
          <Field
            id="password"
            label="Nova lozinka (najmanje 12 znakova)"
            type="password"
            autoComplete="new-password"
            minLength={12}
            required
            value={password}
            onChange={(e) => setPassword(e.target.value)}
          />
          <ErrorMessage error={reset.error} />
          <Button type="submit" className="w-full" disabled={reset.isPending}>
            Sačuvaj
          </Button>
        </form>
      )}
    </Card>
  )
}
