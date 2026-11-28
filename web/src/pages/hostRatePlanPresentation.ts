export type HostRatingContext = {
  name: string | null
  source: string
  hasPublishedPlan: boolean
  restoreNationalTollPrefix: boolean
}

const sourceLabels: Record<string, string> = {
  terminal: 'Explicit phone selection', customer: 'Customer default',
  legacy: 'Existing automatic customer default', none: 'No published customer plan',
  'terminal-unavailable': 'Phone selection unavailable', 'customer-unavailable': 'Customer selection unavailable',
  unmatched: 'Unmatched phone',
}

export function hostRatePlanSourceLabel(source: string) {
  return sourceLabels[source] ?? 'Unrecognized selection source'
}
