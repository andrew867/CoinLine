export const alarmSeverities = [
  { value: 1, label: 'Critical' },
  { value: 2, label: 'Major' },
  { value: 3, label: 'Minor' },
  { value: 4, label: 'Report' },
] as const

export function alarmSeverityLabel(value: number | null | undefined) {
  return alarmSeverities.find(entry => entry.value === value)?.label ?? 'Unclassified'
}
