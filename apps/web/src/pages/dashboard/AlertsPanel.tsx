import { AlertOctagon, AlertTriangle, CheckCircle2 } from 'lucide-react'
import { Link } from 'react-router'
import { useAlerts } from '@/api/hooks'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { cn } from '@/lib/utils'

/** Indicadores de alerta avaliados pela API (limites de host + estado dos containers). */
export function AlertsPanel() {
  const { data, isPending } = useAlerts()
  const alerts = data ?? []

  return (
    <Card>
      <CardHeader>
        <CardTitle>Alertas</CardTitle>
        {alerts.length > 0 && (
          <span className="text-xs text-muted-foreground">
            {alerts.filter((a) => a.severity === 'critical').length} críticos ·{' '}
            {alerts.filter((a) => a.severity === 'warning').length} avisos
          </span>
        )}
      </CardHeader>
      <CardContent>
        {isPending ? (
          <p className="text-sm text-muted-foreground">Avaliando…</p>
        ) : alerts.length === 0 ? (
          <p className="flex items-center gap-2 text-sm text-success">
            <CheckCircle2 className="size-4" /> Tudo certo.
          </p>
        ) : (
          <ul className="space-y-2">
            {alerts.map((a) => {
              const containerId = a.id.split(':')[1]
              const Icon = a.severity === 'critical' ? AlertOctagon : AlertTriangle
              return (
                <li
                  key={a.id}
                  className={cn(
                    'flex items-start gap-3 rounded-md border p-2.5 text-sm',
                    a.severity === 'critical'
                      ? 'border-danger/40 bg-danger/10'
                      : 'border-warning/40 bg-warning/10',
                  )}
                >
                  <Icon
                    className={cn(
                      'mt-0.5 size-4 shrink-0',
                      a.severity === 'critical' ? 'text-danger' : 'text-warning',
                    )}
                  />
                  <div className="min-w-0">
                    <p className="font-medium">
                      {a.id.startsWith('container.') && containerId ? (
                        <Link to={`/containers/${containerId}`} className="hover:underline">
                          {a.title}
                        </Link>
                      ) : (
                        a.title
                      )}
                    </p>
                    <p className="text-muted-foreground">{a.detail}</p>
                  </div>
                </li>
              )
            })}
          </ul>
        )}
      </CardContent>
    </Card>
  )
}
