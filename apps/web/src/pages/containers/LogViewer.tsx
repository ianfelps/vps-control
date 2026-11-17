import { ArrowDownToLine, Pause, Play, Search } from 'lucide-react'
import { useEffect, useMemo, useRef, useState } from 'react'
import { useLogStream } from '@/api/realtime'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { cn } from '@/lib/utils'

const timeFormat = new Intl.DateTimeFormat('pt-BR', {
  hour: '2-digit',
  minute: '2-digit',
  second: '2-digit',
})

/** Logs em tempo real: tail inicial + follow, com filtro, pausa e auto-scroll. */
export function LogViewer({ containerId }: { containerId: string }) {
  const [tail, setTail] = useState(200)
  const [paused, setPaused] = useState(false)
  const [filter, setFilter] = useState('')
  const [follow, setFollow] = useState(true)
  const { lines, error, ended, connected } = useLogStream(containerId, tail, !paused)
  const scrollRef = useRef<HTMLDivElement>(null)

  const visible = useMemo(() => {
    const q = filter.trim().toLowerCase()
    return q ? lines.filter((l) => l.text.toLowerCase().includes(q)) : lines
  }, [lines, filter])

  useEffect(() => {
    const el = scrollRef.current
    if (follow && el) el.scrollTop = el.scrollHeight
  }, [visible, follow])

  return (
    <div className="space-y-3">
      <div className="flex flex-wrap items-center gap-2">
        <div className="relative min-w-40 flex-1">
          <Search className="pointer-events-none absolute left-3 top-1/2 size-4 -translate-y-1/2 text-muted-foreground" />
          <Input
            value={filter}
            onChange={(e) => setFilter(e.target.value)}
            placeholder="Filtrar linhas…"
            className="pl-9"
            aria-label="Filtrar logs"
          />
        </div>
        <select
          value={tail}
          onChange={(e) => setTail(Number(e.target.value))}
          className="h-9 rounded-md border border-border bg-card px-2 text-sm"
          aria-label="Linhas iniciais"
        >
          {[100, 200, 500, 1000, 2000].map((n) => (
            <option key={n} value={n}>
              últimas {n}
            </option>
          ))}
        </select>
        <Button variant="outline" size="sm" onClick={() => setPaused((p) => !p)}>
          {paused ? <Play /> : <Pause />}
          {paused ? 'Retomar' : 'Pausar'}
        </Button>
        <Button
          variant={follow ? 'outline' : 'ghost'}
          size="sm"
          onClick={() => setFollow((f) => !f)}
          aria-pressed={follow}
        >
          <ArrowDownToLine />
          Seguir
        </Button>
        <Badge tone={paused ? 'neutral' : connected && !ended && !error ? 'success' : 'warning'}>
          {paused ? 'pausado' : error ? 'erro' : ended ? 'encerrado' : connected ? 'ao vivo' : 'desconectado'}
        </Badge>
      </div>

      {error && <p className="text-sm text-danger">{error}</p>}

      <div
        ref={scrollRef}
        onScroll={(e) => {
          const el = e.currentTarget
          const atBottom = el.scrollHeight - el.scrollTop - el.clientHeight < 24
          if (atBottom !== follow) setFollow(atBottom)
        }}
        className="h-[60vh] overflow-auto rounded-lg border border-border bg-[oklch(0.17_0.01_260)] p-3 font-mono text-xs leading-relaxed text-[oklch(0.9_0.01_250)]"
        role="log"
        aria-live="off"
      >
        {visible.length === 0 ? (
          <p className="text-[oklch(0.65_0.01_250)]">{paused ? 'Pausado.' : 'Aguardando linhas…'}</p>
        ) : (
          visible.map((l, i) => (
            <div key={i} className="flex gap-3 whitespace-pre-wrap break-all">
              <span className="shrink-0 select-none text-[oklch(0.6_0.01_250)]">
                {l.timestamp ? timeFormat.format(new Date(l.timestamp)) : '--:--:--'}
              </span>
              <span
                className={cn(
                  l.stream === 'stderr' && 'text-[oklch(0.75_0.15_30)]',
                  l.stream === 'system' && 'italic text-[oklch(0.8_0.12_80)]',
                )}
              >
                {l.text}
              </span>
            </div>
          ))
        )}
      </div>
      <p className="text-xs text-muted-foreground">
        {visible.length} linhas exibidas{filter && ` de ${lines.length}`} · máximo de 2000 em memória
      </p>
    </div>
  )
}
