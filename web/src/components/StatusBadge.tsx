import type { CSSProperties } from 'react'

type Variant = 'neutral' | 'ok' | 'warn' | 'bad' | 'info'

const styles: Record<Variant, CSSProperties> = {
  neutral: { background: '#eef1f4', color: '#223', border: '1px solid #ccd' },
  ok: { background: '#e8f7ee', color: '#063', border: '1px solid #9c9' },
  warn: { background: '#fff8e6', color: '#630', border: '1px solid #e6c46a' },
  bad: { background: '#fdecee', color: '#900', border: '1px solid #eaa' },
  info: { background: '#eef6ff', color: '#036', border: '1px solid #9bd' },
}

export function terminalStatusVariant(status: number): Variant {
  switch (status) {
    case 2:
      return 'ok'
    case 3:
      return 'warn'
    case 4:
      return 'info'
    case 5:
      return 'bad'
    default:
      return 'neutral'
  }
}

export function terminalStatusLabel(status: number): string {
  const m: Record<number, string> = {
    0: 'Unknown',
    1: 'Provisioned',
    2: 'Online',
    3: 'Offline',
    4: 'Maintenance',
    5: 'Decommissioned',
  }
  return m[status] ?? `Status ${status}`
}

type BadgeProps = {
  label: string
  variant?: Variant
  title?: string
  'data-testid'?: string
}

export function StatusBadge({
  label,
  variant = 'neutral',
  title,
  'data-testid': testId = 'status-badge',
}: BadgeProps) {
  return (
    <span
      data-testid={testId}
      title={title}
      style={{
        display: 'inline-block',
        fontSize: 12,
        fontWeight: 600,
        padding: '2px 8px',
        borderRadius: 4,
        ...styles[variant],
      }}
    >
      {label}
    </span>
  )
}
