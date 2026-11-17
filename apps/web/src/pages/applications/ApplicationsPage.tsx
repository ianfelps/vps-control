import { Boxes, ExternalLink } from 'lucide-react'
import { Link } from 'react-router'
import { useApplications } from '@/api/hooks'
import { ApplicationStatusBadge } from '@/components/ContainerStatus'
import { Card, CardContent } from '@/components/ui/card'
import { Skeleton } from '@/components/ui/skeleton'
import { formatBytes, formatPercent } from '@/lib/format'

export function ApplicationsPage() {
  const { data, isPending, error } = useApplications()

  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-2xl font-semibold tracking-tight">Aplicações</h1>
        <p className="text-sm text-muted-foreground">
          Projetos Docker Compose detectados pela label{' '}
          <code className="font-mono">com.docker.compose.project</code>.
        </p>
      </div>

      {isPending ? (
        <div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-3">
          {Array.from({ length: 3 }, (_, i) => (
            <Skeleton key={i} className="h-36" />
          ))}
        </div>
      ) : error ? (
        <p className="text-sm text-danger">Falha ao carregar aplicações.</p>
      ) : data.length === 0 ? (
        <Card>
          <CardContent className="flex flex-col items-center gap-2 p-10 text-center text-sm text-muted-foreground">
            <Boxes className="size-8" />
            Nenhum projeto Compose encontrado.
          </CardContent>
        </Card>
      ) : (
        <div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-3">
          {data.map((a) => (
            <Card key={a.slug} className="flex flex-col transition-colors hover:border-primary/50">
              <CardContent className="flex flex-1 flex-col gap-3 p-4">
                <div className="flex items-start justify-between gap-2">
                  <div className="min-w-0">
                    <Link
                      to={`/applications/${encodeURIComponent(a.slug)}`}
                      className="font-semibold hover:text-primary"
                    >
                      {a.displayName}
                    </Link>
                    {a.displayName !== a.slug && (
                      <p className="font-mono text-xs text-muted-foreground">{a.slug}</p>
                    )}
                  </div>
                  <ApplicationStatusBadge status={a.status} />
                </div>
                {a.description && (
                  <p className="line-clamp-2 text-sm text-muted-foreground">{a.description}</p>
                )}
                <div className="mt-auto flex flex-wrap items-center gap-x-4 gap-y-1 text-xs text-muted-foreground tabular-nums">
                  <span>
                    {a.runningCount}/{a.containerCount} rodando
                  </span>
                  <span>CPU {formatPercent(a.cpuPercent)}</span>
                  <span>Mem {formatBytes(a.memoryUsageBytes)}</span>
                  {a.url && (
                    <a
                      href={a.url}
                      target="_blank"
                      rel="noreferrer noopener"
                      className="inline-flex items-center gap-1 text-primary hover:underline"
                    >
                      <ExternalLink className="size-3" /> abrir
                    </a>
                  )}
                </div>
              </CardContent>
            </Card>
          ))}
        </div>
      )}
    </div>
  )
}
