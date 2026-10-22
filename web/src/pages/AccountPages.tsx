import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Link, useNavigate } from 'react-router'
import { authApi } from '../api/auth'
import { useStepUp } from '../auth/stepUpContext'
import { Button, Card, ErrorMessage } from '../components/ui'
import { ProvisioningView } from '../components/ProvisioningView'

export function HomePage({ email }: { email: string }) {
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const withStepUp = useStepUp()
  const provisioning = useMutation({ mutationFn: () => withStepUp(authApi.provisioning) })
  const logout = useMutation({
    mutationFn: authApi.logout,
    onSuccess: () => {
      queryClient.clear()
      navigate('/login')
    },
  })

  return (
    <Card title="Cesio">
      <p className="mb-4 text-sm">Prijavljeni ste kao {email}.</p>
      <div className="flex flex-wrap gap-2">
        <Link
          to="/sessions"
          className="inline-flex h-10 items-center rounded-md border border-slate-300 px-4 text-sm hover:bg-slate-100"
        >
          Sesije
        </Link>
        <Button variant="secondary" onClick={() => provisioning.mutate()}>
          Dodaj uređaj za kodove
        </Button>
        <Button variant="secondary" onClick={() => logout.mutate()}>
          Odjava
        </Button>
      </div>
      {provisioning.data && (
        <div className="mt-4">
          <ProvisioningView provisioning={provisioning.data} />
        </div>
      )}
      <ErrorMessage error={provisioning.error ?? logout.error} />
    </Card>
  )
}

export function SessionsPage() {
  const queryClient = useQueryClient()
  const sessions = useQuery({ queryKey: ['sessions'], queryFn: authApi.sessions })
  const revoke = useMutation({
    mutationFn: authApi.revokeSession,
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['sessions'] }),
  })

  return (
    <Card title="Aktivne sesije">
      <ul className="space-y-3">
        {sessions.data?.map((s) => (
          <li
            key={s.id}
            className="flex items-center justify-between gap-2 border-b border-slate-100 pb-2 text-sm"
          >
            <span>
              {s.userAgent ?? 'Nepoznat uređaj'}
              <br />
              <span className="text-slate-500">
                {s.ipAddress} · poslednja aktivnost{' '}
                {new Date(s.lastSeenAt).toLocaleString('sr-Latn-RS')}
              </span>
            </span>
            {s.current ? (
              <span className="text-slate-500">ova sesija</span>
            ) : (
              <Button variant="secondary" onClick={() => revoke.mutate(s.id)}>
                Odjavi
              </Button>
            )}
          </li>
        ))}
      </ul>
      <ErrorMessage error={sessions.error ?? revoke.error} />
      <Link to="/" className="mt-4 block text-sm text-slate-600 underline">
        Nazad
      </Link>
    </Card>
  )
}
