import { HubConnectionBuilder, HubConnectionState, LogLevel } from '@microsoft/signalr'
import { useQueryClient } from '@tanstack/react-query'
import { useEffect, useState } from 'react'
import type { HostSnapshot, MetricsPoint, SourceStatus } from './client'
import { queryKeys } from './hooks'

/** Mesmo limite do ring buffer da API: 1 h a cada 2 s. */
const MAX_POINTS = 1800

export type StreamState = 'connecting' | 'live' | 'reconnecting' | 'offline'

/** Assina o hub de métricas e injeta cada amostra no cache do TanStack Query. */
export function useMetricsStream(enabled: boolean): StreamState {
  const qc = useQueryClient()
  const [state, setState] = useState<StreamState>('connecting')

  useEffect(() => {
    if (!enabled) return

    const connection = new HubConnectionBuilder()
      .withUrl('/hubs/metrics', { withCredentials: true })
      .withAutomaticReconnect([0, 2000, 5000, 10000, 30000])
      .configureLogging(LogLevel.Warning)
      .build()

    connection.on('snapshot', (snapshot: HostSnapshot, point: MetricsPoint) => {
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
    connection.on('status', (status: SourceStatus) => qc.setQueryData(queryKeys.status, status))
    connection.onreconnecting(() => setState('reconnecting'))
    connection.onreconnected(() => {
      setState('live')
      void qc.invalidateQueries({ queryKey: queryKeys.history })
    })
    connection.onclose(() => setState('offline'))

    let cancelled = false
    connection
      .start()
      .then(() => !cancelled && setState('live'))
      .catch(() => !cancelled && setState('offline'))

    return () => {
      cancelled = true
      if (connection.state !== HubConnectionState.Disconnected) void connection.stop()
    }
  }, [enabled, qc])

  return enabled ? state : 'offline'
}
