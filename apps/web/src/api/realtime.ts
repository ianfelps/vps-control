import {
  HubConnectionBuilder,
  HubConnectionState,
  LogLevel,
  type HubConnection,
  type ISubscription,
} from '@microsoft/signalr'
import { useQueryClient } from '@tanstack/react-query'
import { useEffect, useState, useSyncExternalStore } from 'react'
import type { ContainersSnapshot, HostSnapshot, LogLine, MetricsPoint, SourceStatus } from './client'
import { queryKeys } from './hooks'

/** Mesmo limite do ring buffer da API: 1 h a cada 2 s. */
const MAX_POINTS = 1800

export type StreamState = 'connecting' | 'live' | 'reconnecting' | 'offline'

// Conexão única compartilhada pela aplicação (métricas, containers e streams de log).
let connection: HubConnection | null = null
let state: StreamState = 'offline'
const listeners = new Set<() => void>()

function setState(next: StreamState) {
  state = next
  listeners.forEach((l) => l())
}

function subscribe(listener: () => void) {
  listeners.add(listener)
  return () => listeners.delete(listener)
}

export function useStreamState() {
  return useSyncExternalStore(subscribe, () => state)
}

/** Abre a conexão quando autenticado e injeta os eventos no cache do TanStack Query. */
export function useMetricsStream(enabled: boolean): StreamState {
  const qc = useQueryClient()
  const current = useStreamState()

  useEffect(() => {
    if (!enabled) return

    const conn = new HubConnectionBuilder()
      .withUrl('/hubs/metrics', { withCredentials: true })
      .withAutomaticReconnect([0, 2000, 5000, 10000, 30000])
      .configureLogging(LogLevel.Warning)
      .build()
    connection = conn

    conn.on('snapshot', (snapshot: HostSnapshot, point: MetricsPoint) => {
      qc.setQueryData(queryKeys.snapshot, snapshot)
      qc.setQueryData<MetricsPoint[]>(queryKeys.history, (old = []) => {
        const next = old.length >= MAX_POINTS ? old.slice(old.length - MAX_POINTS + 1) : old.slice()
        next.push(point)
        return next
      })
      qc.setQueryData<SourceStatus>(queryKeys.status, (old) => ({
        source: old?.source ?? '',
        available: true,
        lastSuccessAt: snapshot.timestamp,
        lastError: null,
      }))
    })
    conn.on('status', (status: SourceStatus) => qc.setQueryData(queryKeys.status, status))
    conn.on('containers', (snapshot: ContainersSnapshot) => {
      qc.setQueryData(queryKeys.containers, snapshot)
      void qc.invalidateQueries({ queryKey: queryKeys.applications })
    })
    conn.onreconnecting(() => setState('reconnecting'))
    conn.onreconnected(() => {
      setState('live')
      void qc.invalidateQueries({ queryKey: queryKeys.history })
    })
    conn.onclose(() => setState('offline'))

    let cancelled = false
    setState('connecting')
    conn
      .start()
      .then(() => !cancelled && setState('live'))
      .catch(() => !cancelled && setState('offline'))

    return () => {
      cancelled = true
      if (connection === conn) connection = null
      if (conn.state !== HubConnectionState.Disconnected) void conn.stop()
    }
  }, [enabled, qc])

  return enabled ? current : 'offline'
}

const MAX_LOG_LINES = 2000

/** Logs em tempo real via stream do hub. Mantém no máximo 2000 linhas em memória. */
type LogSession = { key: string; lines: LogLine[]; error: string | null; ended: boolean }

export function useLogStream(id: string, tail: number, enabled: boolean) {
  const live = useStreamState() === 'live'
  // Estado amarrado à "sessão" (container + tail): ao trocar, o estado antigo é ignorado
  // sem precisar resetar com setState dentro do effect.
  const key = `${id}|${tail}`
  const [session, setSession] = useState<LogSession>({ key, lines: [], error: null, ended: false })

  useEffect(() => {
    if (!enabled || !live || !connection) return
    const update = (fn: (s: LogSession) => Partial<LogSession>) =>
      setSession((s) => {
        const base = s.key === key ? s : { key, lines: [], error: null, ended: false }
        return { ...base, ...fn(base) }
      })

    let buffer: LogLine[] = []
    // Agrupa linhas por quadro para não renderizar a cada linha.
    const flush = window.setInterval(() => {
      if (buffer.length === 0) return
      const batch = buffer
      buffer = []
      update((s) => {
        const next = s.lines.concat(batch)
        return { lines: next.length > MAX_LOG_LINES ? next.slice(next.length - MAX_LOG_LINES) : next }
      })
    }, 250)

    // Nova assinatura (inclusive ao retomar após pausa) começa do zero.
    update(() => ({ lines: [], error: null, ended: false }))
    const sub: ISubscription<LogLine> = connection.stream<LogLine>('StreamLogs', id, tail).subscribe({
      next: (line) => buffer.push(line),
      error: (e) =>
        update(() => ({
          error: e instanceof Error ? e.message.replace(/^.*HubException: /, '') : String(e),
        })),
      complete: () => update(() => ({ ended: true })),
    })

    return () => {
      window.clearInterval(flush)
      sub.dispose()
    }
  }, [id, tail, key, enabled, live])

  const current = session.key === key ? session : { lines: [], error: null, ended: false }
  return { lines: current.lines, error: current.error, ended: current.ended, connected: live }
}
