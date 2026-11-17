import { Lock } from 'lucide-react'
import type { ApplicationStatus, ContainerHealth } from '@/api/client'
import { Badge } from '@/components/ui/badge'

export function ContainerStateBadge({ state, health }: { state: string; health: ContainerHealth }) {
  if (state === 'running') {
    if (health === 'unhealthy') return <Dot tone="danger" label="unhealthy" />
    if (health === 'starting') return <Dot tone="warning" label="iniciando" />
    return <Dot tone="success" label={health === 'healthy' ? 'healthy' : 'rodando'} />
  }
  if (state === 'restarting') return <Dot tone="warning" label="reiniciando" />
  if (state === 'paused') return <Dot tone="warning" label="pausado" />
  return <Dot tone="neutral" label={state === 'exited' ? 'parado' : state} />
}

const APP_STATUS: Record<
  ApplicationStatus,
  { tone: 'success' | 'warning' | 'danger' | 'neutral'; label: string }
> = {
  running: { tone: 'success', label: 'Operacional' },
  degraded: { tone: 'warning', label: 'Degradado' },
  stopped: { tone: 'danger', label: 'Parado' },
  missing: { tone: 'neutral', label: 'Sem containers' },
}

export function ApplicationStatusBadge({ status }: { status: ApplicationStatus }) {
  const s = APP_STATUS[status]
  return <Dot tone={s.tone} label={s.label} />
}

export function ProtectedBadge() {
  return (
    <Badge tone="neutral" title="Protegido: o painel não para nem reinicia este container">
      <Lock className="size-3" /> protegido
    </Badge>
  )
}

function Dot({ tone, label }: { tone: 'success' | 'warning' | 'danger' | 'neutral'; label: string }) {
  return (
    <Badge tone={tone}>
      <span className="size-1.5 rounded-full bg-current" />
      {label}
    </Badge>
  )
}
