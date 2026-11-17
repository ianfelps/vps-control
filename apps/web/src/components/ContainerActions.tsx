import { Play, RotateCw, Square } from 'lucide-react'
import { useState } from 'react'
import { ApiError, PERMISSIONS, type ContainerAction } from '@/api/client'
import { useContainerAction, useHasPermission } from '@/api/hooks'
import { Button } from '@/components/ui/button'
import { ConfirmDialog } from '@/components/ui/confirm-dialog'

const LABELS: Record<ContainerAction, { verb: string; confirm: string; destructive: boolean }> = {
  start: { verb: 'Iniciar', confirm: 'Iniciar container', destructive: false },
  stop: { verb: 'Parar', confirm: 'Parar container', destructive: true },
  restart: { verb: 'Reiniciar', confirm: 'Reiniciar container', destructive: true },
}

/** Botões start/stop/restart com confirmação. Oculto sem a permissão containers.manage. */
export function ContainerActions({
  id,
  name,
  running,
  isProtected,
  size = 'sm',
  compact = false,
}: {
  id: string
  name: string
  running: boolean
  isProtected: boolean
  size?: 'sm' | 'default'
  /** Somente ícones (tabelas); o rótulo vai para title/aria-label. */
  compact?: boolean
}) {
  const allowed = useHasPermission(PERMISSIONS.containersManage)
  const action = useContainerAction()
  const [pending, setPending] = useState<ContainerAction | null>(null)
  const [feedback, setFeedback] = useState<{ ok: boolean; text: string } | null>(null)

  if (!allowed) return null

  const confirm = () => {
    if (!pending) return
    const verb = LABELS[pending].verb.toLowerCase()
    action.mutate(
      { id, action: pending },
      {
        onSuccess: () => setFeedback({ ok: true, text: `Comando "${verb}" executado.` }),
        onError: (e) =>
          setFeedback({ ok: false, text: e instanceof ApiError ? e.message : 'Falha ao executar.' }),
        onSettled: () => setPending(null),
      },
    )
  }

  const button = (a: ContainerAction, icon: React.ReactNode, disabled: boolean, title?: string) => (
    <Button
      variant="outline"
      size={compact ? 'icon' : size}
      className={compact ? 'size-8' : undefined}
      onClick={() => {
        setFeedback(null)
        setPending(a)
      }}
      disabled={disabled || action.isPending}
      title={title ?? LABELS[a].verb}
      aria-label={LABELS[a].verb}
    >
      {icon}
      {!compact && <span>{LABELS[a].verb}</span>}
    </Button>
  )

  const protectedTitle = isProtected ? 'Container protegido' : undefined

  return (
    <div className={compact ? 'flex flex-col items-end gap-1' : 'flex flex-wrap items-center gap-2'}>
      <div className={compact ? 'flex flex-nowrap gap-1.5' : 'contents'}>
        {running ? button('stop', <Square />, isProtected, protectedTitle) : button('start', <Play />, false)}
        {button('restart', <RotateCw />, isProtected, protectedTitle)}
      </div>
      {feedback && (
        <span role="status" className={`text-xs ${feedback.ok ? 'text-success' : 'text-danger'}`}>
          {feedback.text}
        </span>
      )}
      <ConfirmDialog
        open={pending !== null}
        title={pending ? LABELS[pending].confirm : ''}
        confirmLabel={pending ? LABELS[pending].verb : ''}
        destructive={pending ? LABELS[pending].destructive : false}
        pending={action.isPending}
        onConfirm={confirm}
        onCancel={() => setPending(null)}
      >
        Confirma <strong className="text-foreground">{pending && LABELS[pending].verb.toLowerCase()}</strong>{' '}
        o container <code className="font-mono text-foreground">{name}</code>? A ação será registrada na
        auditoria.
      </ConfirmDialog>
    </div>
  )
}
