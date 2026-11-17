const UNITS = ['B', 'KB', 'MB', 'GB', 'TB', 'PB'] as const

/** Formata bytes em base 1024: 1536 → "1,5 KB". */
export function formatBytes(bytes: number, fractionDigits = 1): string {
  if (!Number.isFinite(bytes) || bytes <= 0) return '0 B'
  const exponent = Math.min(Math.floor(Math.log(bytes) / Math.log(1024)), UNITS.length - 1)
  const value = bytes / 1024 ** exponent
  const digits = exponent === 0 ? 0 : fractionDigits
  return `${value.toLocaleString('pt-BR', { maximumFractionDigits: digits, minimumFractionDigits: digits })} ${UNITS[exponent]}`
}

export function formatRate(bytesPerSecond: number): string {
  return `${formatBytes(bytesPerSecond)}/s`
}

/** 266400 s → "3d 2h 0m". */
export function formatUptime(totalSeconds: number): string {
  const s = Math.max(0, Math.floor(totalSeconds))
  const days = Math.floor(s / 86400)
  const hours = Math.floor((s % 86400) / 3600)
  const minutes = Math.floor((s % 3600) / 60)
  if (days > 0) return `${days}d ${hours}h ${minutes}m`
  if (hours > 0) return `${hours}h ${minutes}m`
  return `${minutes}m`
}

export function formatPercent(value: number): string {
  return `${value.toLocaleString('pt-BR', { maximumFractionDigits: 1, minimumFractionDigits: 1 })}%`
}

export function percentOf(used: number, total: number): number {
  return total > 0 ? (used / total) * 100 : 0
}

/** Nível de severidade para colorir barras e indicadores. */
export function severity(percent: number): 'ok' | 'warn' | 'critical' {
  if (percent >= 90) return 'critical'
  if (percent >= 75) return 'warn'
  return 'ok'
}
