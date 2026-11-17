import { AlertTriangle, Clock, Cpu, HardDrive, MemoryStick, Network, Server } from 'lucide-react'
import { useMemo, useState, type ReactNode } from 'react'
import { useHistory, useSnapshot, useSourceStatus } from '@/api/hooks'
import type { HostSnapshot } from '@/api/client'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Skeleton } from '@/components/ui/skeleton'
import { formatBytes, formatPercent, formatRate, formatUptime, percentOf } from '@/lib/format'
import { MetricsChart } from './MetricsChart'
import { UsageBar } from './UsageBar'

const RANGES = [
  { label: '5 min', minutes: 5 },
  { label: '15 min', minutes: 15 },
  { label: '1 h', minutes: 60 },
] as const

export function DashboardPage() {
  const snapshot = useSnapshot()
  const history = useHistory()
  const status = useSourceStatus()
  const [range, setRange] = useState<number>(15)

  const points = useMemo(() => {
    const all = history.data ?? []
    const newest = all.at(-1)
    if (!newest) return all
    // Janela relativa à amostra mais recente (pura; independe do relógio do navegador).
    const cutoff = new Date(newest.timestamp).getTime() - range * 60_000
    return all.filter((p) => new Date(p.timestamp).getTime() >= cutoff)
  }, [history.data, range])

  const s = snapshot.data
  const unavailable = status.data && !status.data.available

  return (
    <div className="space-y-6">
      <div className="flex flex-wrap items-end justify-between gap-3">
        <div>
          <h1 className="text-2xl font-semibold tracking-tight">Dashboard</h1>
          <p className="text-sm text-muted-foreground">
            {s
              ? `${s.info.hostname} · ${s.info.osName} · kernel ${s.info.kernelVersion}`
              : 'Carregando host…'}
          </p>
        </div>
      </div>

      {unavailable && (
        <div className="flex items-start gap-3 rounded-lg border border-warning/40 bg-warning/10 p-3 text-sm">
          <AlertTriangle className="mt-0.5 size-4 shrink-0 text-warning" />
          <div>
            <p className="font-medium">Fonte de métricas indisponível ({status.data?.source})</p>
            <p className="text-muted-foreground">
              {status.data?.lastError ?? 'Sem resposta do agente.'}
              {s && ' Exibindo a última amostra recebida.'}
            </p>
          </div>
        </div>
      )}

      {!s ? <StatsSkeleton /> : <StatCards s={s} />}

      <div className="grid gap-4 lg:grid-cols-2">
        <Card>
          <CardHeader>
            <CardTitle>CPU e memória</CardTitle>
            <div className="flex gap-1">
              {RANGES.map((r) => (
                <Button
                  key={r.minutes}
                  size="sm"
                  variant={range === r.minutes ? 'outline' : 'ghost'}
                  onClick={() => setRange(r.minutes)}
                >
                  {r.label}
                </Button>
              ))}
            </div>
          </CardHeader>
          <CardContent>
            <MetricsChart
              data={points}
              domain={[0, 100]}
              formatValue={(v) => `${Math.round(v)}%`}
              series={[
                { key: 'cpuPercent', label: 'CPU', color: 'var(--color-chart-1)' },
                { key: 'memoryPercent', label: 'Memória', color: 'var(--color-chart-2)' },
              ]}
            />
          </CardContent>
        </Card>
        <Card>
          <CardHeader>
            <CardTitle>Tráfego de rede</CardTitle>
          </CardHeader>
          <CardContent>
            <MetricsChart
              data={points}
              formatValue={formatRate}
              series={[
                { key: 'rxBytesPerSecond', label: 'Recebido', color: 'var(--color-chart-1)' },
                { key: 'txBytesPerSecond', label: 'Enviado', color: 'var(--color-chart-3)' },
              ]}
            />
          </CardContent>
        </Card>
      </div>

      {s && <DisksAndNetwork s={s} />}
    </div>
  )
}

function StatCard({
  title,
  icon,
  value,
  detail,
  children,
}: {
  title: string
  icon: ReactNode
  value: string
  detail?: string
  children?: ReactNode
}) {
  return (
    <Card>
      <CardHeader>
        <CardTitle>{title}</CardTitle>
        <span className="text-muted-foreground [&_svg]:size-4">{icon}</span>
      </CardHeader>
      <CardContent className="space-y-2">
        <div className="text-2xl font-semibold tabular-nums">{value}</div>
        {children}
        {detail && <p className="text-xs text-muted-foreground">{detail}</p>}
      </CardContent>
    </Card>
  )
}

function StatCards({ s }: { s: HostSnapshot }) {
  const memUsed = s.memory.totalBytes - s.memory.availableBytes
  const memPct = percentOf(memUsed, s.memory.totalBytes)
  const swapUsed = s.memory.swapTotalBytes - s.memory.swapFreeBytes
  const root = s.disks.find((d) => d.mountPoint === '/') ?? s.disks[0]
  const rootPct = root ? percentOf(root.totalBytes - root.freeBytes, root.totalBytes) : 0

  return (
    <div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-4">
      <StatCard
        title="CPU"
        icon={<Cpu />}
        value={formatPercent(s.cpu.usagePercent)}
        detail={`${s.info.logicalCores} núcleos · load ${s.cpu.load1.toFixed(2)} / ${s.cpu.load5.toFixed(2)} / ${s.cpu.load15.toFixed(2)}`}
      >
        <UsageBar percent={s.cpu.usagePercent} />
      </StatCard>
      <StatCard
        title="Memória"
        icon={<MemoryStick />}
        value={formatPercent(memPct)}
        detail={`${formatBytes(memUsed)} de ${formatBytes(s.memory.totalBytes)} · swap ${formatBytes(swapUsed)}`}
      >
        <UsageBar percent={memPct} />
      </StatCard>
      <StatCard
        title={`Disco ${root?.mountPoint ?? ''}`}
        icon={<HardDrive />}
        value={formatPercent(rootPct)}
        detail={
          root ? `${formatBytes(root.freeBytes)} livres de ${formatBytes(root.totalBytes)}` : 'Sem discos'
        }
      >
        <UsageBar percent={rootPct} />
      </StatCard>
      <StatCard
        title="Uptime"
        icon={<Clock />}
        value={formatUptime(s.info.uptimeSeconds ?? 0)}
        detail="Containers: disponível na fase 3"
      >
        <div className="flex items-center gap-2 text-xs text-muted-foreground">
          <Server className="size-3.5" /> {s.info.hostname}
        </div>
      </StatCard>
    </div>
  )
}

function DisksAndNetwork({ s }: { s: HostSnapshot }) {
  return (
    <div className="grid gap-4 lg:grid-cols-2">
      <Card>
        <CardHeader>
          <CardTitle>Discos</CardTitle>
          <HardDrive className="size-4 text-muted-foreground" />
        </CardHeader>
        <CardContent className="space-y-4">
          {s.disks.map((d) => {
            const pct = percentOf(d.totalBytes - d.freeBytes, d.totalBytes)
            return (
              <div key={d.mountPoint} className="space-y-1.5">
                <div className="flex justify-between gap-2 text-sm">
                  <span className="truncate font-mono">{d.mountPoint}</span>
                  <span className="shrink-0 text-muted-foreground tabular-nums">
                    {formatBytes(d.totalBytes - d.freeBytes)} / {formatBytes(d.totalBytes)} · {d.fileSystem}
                  </span>
                </div>
                <UsageBar percent={pct} />
              </div>
            )
          })}
        </CardContent>
      </Card>
      <Card>
        <CardHeader>
          <CardTitle>Interfaces de rede</CardTitle>
          <Network className="size-4 text-muted-foreground" />
        </CardHeader>
        <CardContent>
          <div className="overflow-x-auto">
            <table className="w-full text-sm">
              <thead className="text-left text-xs text-muted-foreground">
                <tr>
                  <th className="pb-2 font-medium">Interface</th>
                  <th className="pb-2 text-right font-medium">↓ Taxa</th>
                  <th className="pb-2 text-right font-medium">↑ Taxa</th>
                  <th className="pb-2 text-right font-medium">Total ↓/↑</th>
                </tr>
              </thead>
              <tbody className="tabular-nums">
                {s.networks.map((n) => (
                  <tr key={n.interface} className="border-t border-border">
                    <td className="py-2 font-mono">{n.interface}</td>
                    <td className="py-2 text-right">{formatRate(n.rxBytesPerSecond)}</td>
                    <td className="py-2 text-right">{formatRate(n.txBytesPerSecond)}</td>
                    <td className="py-2 text-right text-muted-foreground">
                      {formatBytes(n.rxBytesTotal)} / {formatBytes(n.txBytesTotal)}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </CardContent>
      </Card>
    </div>
  )
}

function StatsSkeleton() {
  return (
    <div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-4">
      {Array.from({ length: 4 }, (_, i) => (
        <Card key={i} className="space-y-3 p-4">
          <Skeleton className="h-4 w-20" />
          <Skeleton className="h-7 w-24" />
          <Skeleton className="h-2 w-full" />
        </Card>
      ))}
    </div>
  )
}
