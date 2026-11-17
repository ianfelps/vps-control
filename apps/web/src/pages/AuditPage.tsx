import { useAudit } from '@/api/hooks'
import { Badge } from '@/components/ui/badge'
import { Card, CardContent } from '@/components/ui/card'
import { Skeleton } from '@/components/ui/skeleton'

const dateFormat = new Intl.DateTimeFormat('pt-BR', { dateStyle: 'short', timeStyle: 'medium' })

export function AuditPage() {
  const { data, isPending, error } = useAudit(200)

  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-2xl font-semibold tracking-tight">Auditoria</h1>
        <p className="text-sm text-muted-foreground">
          Registro append-only de autenticação e ações administrativas.
        </p>
      </div>
      <Card>
        <CardContent className="overflow-x-auto p-0">
          {isPending ? (
            <div className="space-y-2 p-4">
              {Array.from({ length: 6 }, (_, i) => (
                <Skeleton key={i} className="h-6 w-full" />
              ))}
            </div>
          ) : error ? (
            <p className="p-4 text-sm text-danger">Falha ao carregar auditoria.</p>
          ) : data.length === 0 ? (
            <p className="p-4 text-sm text-muted-foreground">Nenhum registro.</p>
          ) : (
            <table className="w-full text-sm">
              <thead className="border-b border-border text-left text-xs text-muted-foreground">
                <tr>
                  <th className="px-4 py-3 font-medium">Quando</th>
                  <th className="px-4 py-3 font-medium">Quem</th>
                  <th className="px-4 py-3 font-medium">Ação</th>
                  <th className="px-4 py-3 font-medium">Alvo</th>
                  <th className="px-4 py-3 font-medium">Resultado</th>
                  <th className="px-4 py-3 font-medium">Origem</th>
                </tr>
              </thead>
              <tbody>
                {data.map((a) => (
                  <tr key={a.id} className="border-b border-border last:border-0">
                    <td className="whitespace-nowrap px-4 py-2 tabular-nums">
                      {dateFormat.format(new Date(a.timestamp))}
                    </td>
                    <td className="px-4 py-2">{a.actor}</td>
                    <td className="px-4 py-2 font-mono text-xs">{a.action}</td>
                    <td className="px-4 py-2 text-muted-foreground">{a.target ?? '—'}</td>
                    <td className="px-4 py-2">
                      <Badge tone={a.result === 'success' ? 'success' : 'danger'}>{a.result}</Badge>
                    </td>
                    <td className="px-4 py-2 font-mono text-xs text-muted-foreground">{a.remoteIp ?? '—'}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
        </CardContent>
      </Card>
    </div>
  )
}
