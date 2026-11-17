import { cn } from '@/lib/utils'
import { severity } from '@/lib/format'

const TONE = {
  ok: 'bg-success',
  warn: 'bg-warning',
  critical: 'bg-danger',
} as const

export function UsageBar({ percent, className }: { percent: number; className?: string }) {
  const clamped = Math.min(100, Math.max(0, percent))
  return (
    <div
      className={cn('h-2 w-full overflow-hidden rounded-full bg-muted', className)}
      role="progressbar"
      aria-valuenow={Math.round(clamped)}
      aria-valuemin={0}
      aria-valuemax={100}
    >
      <div
        className={cn('h-full rounded-full transition-all', TONE[severity(clamped)])}
        style={{ width: `${clamped}%` }}
      />
    </div>
  )
}
