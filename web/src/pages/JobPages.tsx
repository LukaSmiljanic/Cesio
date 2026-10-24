import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from '../api/client'
import { useStepUp } from '../auth/stepUpContext'
import { Button, Card, ErrorMessage } from '../components/ui'

interface JobStatus {
  name: string
  intervalMinutes: number
  lastStartedAt: string | null
  lastStatus: string | null
  lastItems: number | null
  lastError: string | null
  failuresLast24h: number
}

/** Operations: the scheduled jobs with their last run, and a manual run (task 020). */
export function AdminJobsPage() {
  const queryClient = useQueryClient()
  const withStepUp = useStepUp()
  const jobs = useQuery({
    queryKey: ['admin-jobs'],
    queryFn: () => api<JobStatus[]>('GET', '/admin/jobs'),
  })
  const run = useMutation({
    mutationFn: (name: string) =>
      withStepUp(() => api('POST', `/admin/jobs/${encodeURIComponent(name)}/run`)),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['admin-jobs'] }),
  })
  return (
    <Card title="Zakazani poslovi">
      <ErrorMessage error={jobs.error ?? run.error} />
      <ul className="space-y-2 text-sm">
        {jobs.data?.map((j) => (
          <li key={j.name} className="flex items-center justify-between gap-2">
            <span>
              <span className="font-medium">{j.name}</span> · na {j.intervalMinutes} min ·{' '}
              {j.lastStartedAt
                ? `${new Date(j.lastStartedAt).toLocaleString('sr-RS')} ${j.lastStatus} (${j.lastItems ?? 0})`
                : 'nije pokretan'}
              {j.failuresLast24h > 0 && ` · grešaka za 24 h: ${j.failuresLast24h}`}
            </span>
            <Button variant="secondary" onClick={() => run.mutate(j.name)} disabled={run.isPending}>
              Pokreni
            </Button>
          </li>
        ))}
      </ul>
    </Card>
  )
}
