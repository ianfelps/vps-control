import { AlertTriangle, Search } from 'lucide-react'
import { useMemo, useState } from 'react'
import { Link } from 'react-router'
import type { ContainerView } from '@/api/client'
import { selectContainerCounts, useContainers } from '@/api/hooks'
import { ContainerActions } from '@/components/ContainerActions'
import { ContainerStateBadge, ProtectedBadge } from '@/components/ContainerStatus'
import { Button } from '@/components/ui/button'
import { Card, CardContent } from '@/components/ui/card'
import { Input } from '@/components/ui/input'
import { Skeleton } from '@/components/ui/skeleton'
import { formatBytes, formatPercent } from '@/lib/format'

type StateFilter = 'all' | 'running' | 'stopped'

export function ContainersPage() {
  const { data, isPending, error } = useContainers()
  const [query, setQuery] = useState('')
  const [filter, setFilter] = useState<StateFilter>('all')
  const [grouped, setGrouped] = useState(true)
  const counts = selectContainerCounts(data)

  const filtered = useMemo(() => {
    const q = query.trim().toLowerCase()
    return (data?.items ?? []).filter((c) => {
      if (filter === 'running' && c.state !== 'running') return false
      if (filter === 'stopped' && c.state === 'running') return false
      if (!q) return true
      return [c.name, c.image, c.composeProject ?? '', c.composeService ?? '', c.shortId].some((v) =>
        v.toLowerCase().includes(q),
      )
    })
  }, [data, query, filter])

  const groups = useMemo(() => {
    if (!grouped) return [{ key: '', label: null as string | null, items: filtered }]
    const map = new Map<string, ContainerView[]>()
    for (const c of filtered) {
      const key = c.composeProject ?? ''
      map.set(key, [...(map.get(key) ?? []), c])
    }
    return [...map.entries()]
      .sort(([a], [b]) => (a === '' ? 1 : b === '' ? -1 : a.localeCompare(b)))
      .map(([key, items]) => ({ key, label: key || 'Sem projeto Compose', items }))
  }, [filtered, grouped])

  return (
    <div className="space-y-6">
      <div className="flex flex-wrap items-end justify-between gap-3">
        <div>
          <h1 className="text-2xl font-semibold tracking-tight">Containers</h1>
          <p className="text-sm text-muted-foreground">
            {counts.running} rodando · {counts.stopped} parados
            {counts.unhealthy > 0 && <span className="text-danger"> · {counts.unhealthy} não saudáveis</span>}
          </p>
        </div>
      </div>

      {data?.error && (
        <div className="flex items-start gap-3 rounded-lg border border-warning/40 bg-warning/10 p-3 text-sm">
          <AlertTriangle className="mt-0.5 size-4 shrink-0 text-warning" />
          <p>{data.error} Exibindo a última lista conhecida.</p>
        </div>
      )}

      <div className="flex flex-wrap items-center gap-2">
        <div className="relative min-w-48 flex-1">
          <Search className="pointer-events-none absolute left-3 top-1/2 size-4 -translate-y-1/2 text-muted-foreground" />
          <Input
            value={query}
            onChange={(e) => setQuery(e.target.value)}
            placeholder="Buscar por nome, imagem, projeto…"
            className="pl-9"
            aria-label="Buscar containers"
          />
        </div>
        <div className="flex gap-1" role="group" aria-label="Filtrar por estado">
          {(
            [
              ['all', 'Todos'],
              ['running', 'Rodando'],
              ['stopped', 'Parados'],
            ] as const
          ).map(([value, label]) => (
            <Button
              key={value}
              size="sm"
              variant={filter === value ? 'outline' : 'ghost'}
              onClick={() => setFilter(value)}
              aria-pressed={filter === value}
            >
              {label}
            </Button>
          ))}
        </div>
        <Button size="sm" variant="ghost" onClick={() => setGrouped((g) => !g)} aria-pressed={grouped}>
          {grouped ? 'Agrupado por projeto' : 'Lista simples'}
        </Button>
      </div>

      {isPending ? (
        <Card className="space-y-3 p-4">
          {Array.from({ length: 5 }, (_, i) => (
            <Skeleton key={i} className="h-10 w-full" />
          ))}
        </Card>
      ) : error ? (
        <p className="text-sm text-danger">Falha ao carregar containers.</p>
      ) : filtered.length === 0 ? (
        <Card>
          <CardContent className="p-8 text-center text-sm text-muted-foreground">
            Nenhum container encontrado.
          </CardContent>
        </Card>
      ) : (
        groups.map((g) => (
          <section key={g.key} className="space-y-2">
            {g.label && (
              <h2 className="flex items-center gap-2 text-sm font-medium text-muted-foreground">
                {g.key ? (
                  <Link to={`/applications/${encodeURIComponent(g.key)}`} className="hover:text-primary">
                    {g.label}
                  </Link>
                ) : (
                  g.label
                )}
                <span className="text-xs">({g.items.length})</span>
              </h2>
            )}
            <Card className="overflow-hidden">
              <ContainerTable items={g.items} />
            </Card>
          </section>
        ))
      )}
    </div>
  )
}

export function ContainerTable({ items }: { items: ContainerView[] }) {
  return (
    <>
      {/* Desktop */}
      <table className="hidden w-full text-sm lg:table">
        <thead className="border-b border-border text-left text-xs text-muted-foreground">
          <tr>
            <th className="px-4 py-2.5 font-medium">Container</th>
            <th className="px-4 py-2.5 font-medium">Estado</th>
            <th className="px-4 py-2.5 text-right font-medium">CPU</th>
            <th className="px-4 py-2.5 text-right font-medium">Memória</th>
            <th className="px-4 py-2.5 font-medium">Portas</th>
            <th className="px-4 py-2.5 text-right font-medium">Ações</th>
          </tr>
        </thead>
        <tbody>
          {items.map((c) => (
            <tr key={c.id} className="border-b border-border last:border-0 hover:bg-muted/40">
              <td className="max-w-72 px-4 py-2.5">
                <Link to={`/containers/${c.id}`} className="font-medium hover:text-primary">
                  {c.name}
                </Link>
                <div className="truncate font-mono text-xs text-muted-foreground" title={c.image}>
                  {c.image}
                </div>
              </td>
              <td className="px-4 py-2.5">
                <div className="flex flex-wrap items-center gap-1.5">
                  <ContainerStateBadge state={c.state} health={c.health} />
                  {c.protected && <ProtectedBadge />}
                </div>
                <div className="mt-0.5 text-xs text-muted-foreground">{c.status}</div>
              </td>
              <td className="px-4 py-2.5 text-right tabular-nums">
                {c.stats ? formatPercent(c.stats.cpuPercent) : '—'}
              </td>
              <td className="px-4 py-2.5 text-right tabular-nums">
                {c.stats ? formatBytes(c.stats.memoryUsageBytes) : '—'}
              </td>
              <td className="px-4 py-2.5 font-mono text-xs text-muted-foreground">
                <Ports ports={c.ports} />
              </td>
              <td className="px-4 py-2.5">
                <div className="flex justify-end">
                  <ContainerActions
                    id={c.id}
                    name={c.name}
                    running={c.state === 'running'}
                    isProtected={c.protected}
                    compact
                  />
                </div>
              </td>
            </tr>
          ))}
        </tbody>
      </table>

      {/* Mobile */}
      <ul className="divide-y divide-border lg:hidden">
        {items.map((c) => (
          <li key={c.id} className="space-y-2 p-4">
            <div className="flex items-start justify-between gap-2">
              <div className="min-w-0">
                <Link to={`/containers/${c.id}`} className="font-medium hover:text-primary">
                  {c.name}
                </Link>
                <div className="truncate font-mono text-xs text-muted-foreground">{c.image}</div>
              </div>
              <ContainerStateBadge state={c.state} health={c.health} />
            </div>
            <div className="flex flex-wrap gap-x-4 text-xs text-muted-foreground tabular-nums">
              <span>CPU {c.stats ? formatPercent(c.stats.cpuPercent) : '—'}</span>
              <span>Mem {c.stats ? formatBytes(c.stats.memoryUsageBytes) : '—'}</span>
              {c.protected && <span>protegido</span>}
            </div>
            <ContainerActions
              id={c.id}
              name={c.name}
              running={c.state === 'running'}
              isProtected={c.protected}
            />
          </li>
        ))}
      </ul>
    </>
  )
}

function Ports({ ports }: { ports: ContainerView['ports'] }) {
  if (ports.length === 0) return <>—</>
  return (
    <div className="space-y-0.5">
      {ports.slice(0, 3).map((p) => (
        <div key={`${p.privatePort}-${p.publicPort}-${p.type}`}>
          {p.publicPort ? `${p.hostIp ?? '0.0.0.0'}:${p.publicPort} → ` : ''}
          {p.privatePort}/{p.type}
        </div>
      ))}
      {ports.length > 3 && <div>+{ports.length - 3}</div>}
    </div>
  )
}
