import { ArrowLeft, Eye, Lock } from 'lucide-react'
import { useState, type ReactNode } from 'react'
import { Link, useParams } from 'react-router'
import { ApiError, PERMISSIONS } from '@/api/client'
import { useContainer, useContainers, useHasPermission, useRevealEnv } from '@/api/hooks'
import { ContainerActions } from '@/components/ContainerActions'
import { ContainerStateBadge, ProtectedBadge } from '@/components/ContainerStatus'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'
import { Skeleton } from '@/components/ui/skeleton'
import { Tabs } from '@/components/ui/tabs'
import { formatBytes, formatPercent } from '@/lib/format'
import { LogViewer } from './LogViewer'

type Tab = 'overview' | 'logs' | 'config'

const dateFormat = new Intl.DateTimeFormat('pt-BR', { dateStyle: 'short', timeStyle: 'medium' })
const fmtDate = (v: string | null | undefined) => (v ? dateFormat.format(new Date(v)) : '—')

export function ContainerDetailPage() {
  const { id = '' } = useParams()
  const { data: details, isPending, error } = useContainer(id)
  const { data: list } = useContainers()
  const [tab, setTab] = useState<Tab>('overview')
  const live = list?.items.find((c) => c.id === details?.summary.id)

  if (isPending) {
    return (
      <div className="space-y-4">
        <Skeleton className="h-8 w-64" />
        <Skeleton className="h-40 w-full" />
      </div>
    )
  }

  if (error || !details) {
    return (
      <div className="space-y-3 py-10 text-center">
        <p className="font-medium">
          {error instanceof ApiError && error.status === 404
            ? 'Container não encontrado.'
            : 'Falha ao carregar o container.'}
        </p>
        <Link to="/containers" className="text-sm text-primary hover:underline">
          Voltar para containers
        </Link>
      </div>
    )
  }

  const s = details.summary
  // Estado ao vivo vem da lista (SignalR); detalhes podem estar alguns segundos atrás.
  const state = live?.state ?? s.state
  const health = live?.health ?? s.health
  const stats = live?.stats

  return (
    <div className="space-y-6">
      <div className="space-y-3">
        <Link
          to="/containers"
          className="inline-flex items-center gap-1 text-sm text-muted-foreground hover:text-foreground"
        >
          <ArrowLeft className="size-4" /> Containers
        </Link>
        <div className="flex flex-wrap items-start justify-between gap-3">
          <div className="min-w-0 space-y-1">
            <h1 className="flex flex-wrap items-center gap-2 text-2xl font-semibold tracking-tight">
              <span className="truncate">{s.name}</span>
              <ContainerStateBadge state={state} health={health} />
              {s.protected && <ProtectedBadge />}
            </h1>
            <p className="truncate font-mono text-sm text-muted-foreground">{s.image}</p>
            {s.composeProject && (
              <p className="text-sm text-muted-foreground">
                Projeto{' '}
                <Link
                  to={`/applications/${encodeURIComponent(s.composeProject)}`}
                  className="text-primary hover:underline"
                >
                  {s.composeProject}
                </Link>
                {s.composeService && ` · serviço ${s.composeService}`}
              </p>
            )}
          </div>
          <ContainerActions
            id={s.id}
            name={s.name}
            running={state === 'running'}
            isProtected={s.protected}
            size="default"
          />
        </div>
      </div>

      <Tabs
        value={tab}
        onChange={setTab}
        items={[
          { value: 'overview', label: 'Visão geral' },
          { value: 'logs', label: 'Logs' },
          { value: 'config', label: 'Configuração' },
        ]}
      />

      {tab === 'overview' && (
        <div className="grid gap-4 lg:grid-cols-2">
          <Card>
            <CardHeader>
              <CardTitle>Estado</CardTitle>
            </CardHeader>
            <CardContent>
              <dl className="grid grid-cols-[auto_1fr] gap-x-6 gap-y-2 text-sm">
                <Row label="ID">
                  <code className="break-all font-mono text-xs">{s.id}</code>
                </Row>
                <Row label="Status">{live?.status ?? s.status}</Row>
                <Row label="Criado">{fmtDate(s.createdAt)}</Row>
                <Row label="Iniciado">{fmtDate(details.startedAt)}</Row>
                {state !== 'running' && <Row label="Encerrado">{fmtDate(details.finishedAt)}</Row>}
                <Row label="Código de saída">{details.exitCode}</Row>
                <Row label="Reinícios">{details.restartCount}</Row>
                <Row label="Restart policy">{details.restartPolicy}</Row>
              </dl>
            </CardContent>
          </Card>
          <Card>
            <CardHeader>
              <CardTitle>Recursos</CardTitle>
            </CardHeader>
            <CardContent>
              {stats ? (
                <dl className="grid grid-cols-[auto_1fr] gap-x-6 gap-y-2 text-sm tabular-nums">
                  <Row label="CPU">{formatPercent(stats.cpuPercent)}</Row>
                  <Row label="Memória">
                    {formatBytes(stats.memoryUsageBytes)} de {formatBytes(stats.memoryLimitBytes)} (
                    {formatPercent(stats.memoryPercent ?? 0)})
                  </Row>
                  <Row label="Rede ↓/↑">
                    {formatBytes(stats.networkRxBytes)} / {formatBytes(stats.networkTxBytes)}
                  </Row>
                  <Row label="Disco leitura/escrita">
                    {formatBytes(stats.blockReadBytes)} / {formatBytes(stats.blockWriteBytes)}
                  </Row>
                  <Row label="Processos">{stats.pids}</Row>
                </dl>
              ) : (
                <p className="text-sm text-muted-foreground">
                  {state === 'running' ? 'Coletando estatísticas…' : 'Container parado.'}
                </p>
              )}
            </CardContent>
          </Card>
          <Card>
            <CardHeader>
              <CardTitle>Portas</CardTitle>
            </CardHeader>
            <CardContent className="font-mono text-sm">
              {s.ports.length === 0 ? (
                <span className="text-muted-foreground">Nenhuma porta publicada</span>
              ) : (
                s.ports.map((p) => (
                  <div key={`${p.privatePort}-${p.publicPort}-${p.type}`}>
                    {p.publicPort ? `${p.hostIp ?? '0.0.0.0'}:${p.publicPort} → ` : ''}
                    {p.privatePort}/{p.type}
                  </div>
                ))
              )}
            </CardContent>
          </Card>
          <Card>
            <CardHeader>
              <CardTitle>Redes</CardTitle>
            </CardHeader>
            <CardContent className="space-y-1 text-sm">
              {details.networks.map((n) => (
                <div key={n.name} className="flex justify-between gap-2">
                  <span className="font-mono">{n.name}</span>
                  <span className="text-muted-foreground">{n.ipAddress ?? '—'}</span>
                </div>
              ))}
            </CardContent>
          </Card>
        </div>
      )}

      {tab === 'logs' && <LogViewer containerId={s.id} />}

      {tab === 'config' && <ConfigTab id={s.id} />}
    </div>
  )
}

function ConfigTab({ id }: { id: string }) {
  const { data: details } = useContainer(id)
  const canReveal = useHasPermission(PERMISSIONS.secretsReveal)
  const reveal = useRevealEnv(id)
  const [confirming, setConfirming] = useState(false)
  if (!details) return null
  const hasMasked = details.env.some((e) => e.masked)

  return (
    <div className="space-y-4">
      <Card>
        <CardHeader>
          <CardTitle>Comando</CardTitle>
        </CardHeader>
        <CardContent>
          <code className="block break-all rounded-md bg-muted p-2 font-mono text-xs">
            {details.command || '—'}
          </code>
        </CardContent>
      </Card>

      <Card>
        <CardHeader>
          <CardTitle>Variáveis de ambiente</CardTitle>
          {hasMasked && canReveal && (
            <Button variant="outline" size="sm" onClick={() => setConfirming(true)}>
              <Eye /> Revelar valores
            </Button>
          )}
        </CardHeader>
        <CardContent className="overflow-x-auto">
          <table className="w-full text-xs">
            <tbody className="font-mono">
              {details.env.map((e) => (
                <tr key={e.name} className="border-b border-border last:border-0">
                  <td className="py-1.5 pr-4 align-top font-medium">{e.name}</td>
                  <td className="break-all py-1.5 text-muted-foreground">
                    {e.masked ? (
                      <span className="inline-flex items-center gap-1">
                        <Lock className="size-3" /> {e.value}
                      </span>
                    ) : (
                      e.value
                    )}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
          {reveal.error && (
            <p className="mt-2 text-sm text-danger">Falha ao revelar: {reveal.error.message}</p>
          )}
        </CardContent>
      </Card>

      <Card>
        <CardHeader>
          <CardTitle>Volumes e montagens</CardTitle>
        </CardHeader>
        <CardContent className="overflow-x-auto">
          {details.mounts.length === 0 ? (
            <p className="text-sm text-muted-foreground">Nenhuma montagem.</p>
          ) : (
            <table className="w-full text-xs">
              <thead className="text-left text-muted-foreground">
                <tr>
                  <th className="pb-2 font-medium">Tipo</th>
                  <th className="pb-2 font-medium">Origem</th>
                  <th className="pb-2 font-medium">Destino</th>
                  <th className="pb-2 font-medium">Modo</th>
                </tr>
              </thead>
              <tbody className="font-mono">
                {details.mounts.map((m) => (
                  <tr key={m.destination} className="border-t border-border">
                    <td className="py-1.5 pr-3">{m.type}</td>
                    <td className="break-all py-1.5 pr-3">{m.source}</td>
                    <td className="break-all py-1.5 pr-3">{m.destination}</td>
                    <td className="py-1.5">{m.readWrite ? 'rw' : 'ro'}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
        </CardContent>
      </Card>

      <Card>
        <CardHeader>
          <CardTitle>Labels</CardTitle>
        </CardHeader>
        <CardContent className="space-y-1 font-mono text-xs">
          {Object.entries(details.labels).length === 0 ? (
            <span className="text-muted-foreground">Nenhuma label.</span>
          ) : (
            Object.entries(details.labels)
              .sort(([a], [b]) => a.localeCompare(b))
              .map(([k, v]) => (
                <div key={k} className="break-all">
                  <span className="font-medium">{k}</span>
                  <span className="text-muted-foreground">={v}</span>
                </div>
              ))
          )}
        </CardContent>
      </Card>

      <ConfirmDialog
        open={confirming}
        title="Revelar variáveis sensíveis"
        confirmLabel="Revelar"
        pending={reveal.isPending}
        onConfirm={() => reveal.mutate(undefined, { onSettled: () => setConfirming(false) })}
        onCancel={() => setConfirming(false)}
      >
        Os valores de senhas, tokens e chaves serão exibidos nesta tela. A ação será registrada na auditoria.
      </ConfirmDialog>
    </div>
  )
}

function Row({ label, children }: { label: string; children: ReactNode }) {
  return (
    <>
      <dt className="text-muted-foreground">{label}</dt>
      <dd className="min-w-0">{children}</dd>
    </>
  )
}
