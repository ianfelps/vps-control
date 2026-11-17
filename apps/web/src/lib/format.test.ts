import { describe, expect, it } from 'vitest'
import { formatBytes, formatUptime, percentOf, severity } from './format'

describe('formatBytes', () => {
  it('handles zero and invalid input', () => {
    expect(formatBytes(0)).toBe('0 B')
    expect(formatBytes(-5)).toBe('0 B')
    expect(formatBytes(Number.NaN)).toBe('0 B')
  })

  it('uses base 1024 with pt-BR decimals', () => {
    expect(formatBytes(512)).toBe('512 B')
    expect(formatBytes(1536)).toBe('1,5 KB')
    expect(formatBytes(8 * 1024 ** 3)).toBe('8,0 GB')
  })
})

describe('formatUptime', () => {
  it('formats days, hours and minutes', () => {
    expect(formatUptime(59)).toBe('0m')
    expect(formatUptime(3 * 3600 + 120)).toBe('3h 2m')
    expect(formatUptime(3 * 86400 + 2 * 3600)).toBe('3d 2h 0m')
  })
})

describe('severity', () => {
  it('maps thresholds', () => {
    expect(severity(10)).toBe('ok')
    expect(severity(75)).toBe('warn')
    expect(severity(95)).toBe('critical')
    expect(percentOf(1, 0)).toBe(0)
  })
})
