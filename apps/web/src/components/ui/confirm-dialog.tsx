import { useEffect, useRef, type ReactNode } from 'react'
import { Button } from './button'

/** Diálogo modal de confirmação (elemento &lt;dialog&gt; nativo: foco preso e Esc fecham). */
export function ConfirmDialog({
  open,
  title,
  children,
  confirmLabel,
  destructive,
  pending,
  onConfirm,
  onCancel,
}: {
  open: boolean
  title: string
  children: ReactNode
  confirmLabel: string
  destructive?: boolean
  pending?: boolean
  onConfirm: () => void
  onCancel: () => void
}) {
  const ref = useRef<HTMLDialogElement>(null)

  useEffect(() => {
    const dialog = ref.current
    if (!dialog) return
    if (open && !dialog.open) dialog.showModal()
    if (!open && dialog.open) dialog.close()
  }, [open])

  return (
    <dialog
      ref={ref}
      onCancel={(e) => {
        e.preventDefault()
        if (!pending) onCancel()
      }}
      className="m-auto w-[min(28rem,calc(100vw-2rem))] rounded-xl border border-border bg-card p-0 text-foreground shadow-xl backdrop:bg-black/50"
    >
      <div className="space-y-4 p-5">
        <h2 className="text-lg font-semibold">{title}</h2>
        <div className="text-sm text-muted-foreground">{children}</div>
        <div className="flex justify-end gap-2">
          <Button variant="outline" onClick={onCancel} disabled={pending}>
            Cancelar
          </Button>
          <Button
            variant={destructive ? 'danger' : 'default'}
            onClick={onConfirm}
            disabled={pending}
            autoFocus
          >
            {pending ? 'Executando…' : confirmLabel}
          </Button>
        </div>
      </div>
    </dialog>
  )
}
