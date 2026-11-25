type ValueReader = (key: string) => string | null | undefined

type RatingAction =
  | { kind: 'external' }
  | { kind: 'reject' }
  | { kind: 'tariff'; row: number }
  | { kind: 'unknown' }

export type RegionalRatingResult = {
  action: RatingAction
  source: string
  section?: string
  fallback?: boolean
  duplicateSlots?: number[]
  note?: string
}

function numeric(value: string | null | undefined, max: number): number | null {
  if (value == null || !/^\d+$/.test(value)) return null
  const parsed = Number(value)
  return Number.isInteger(parsed) && parsed <= max ? parsed : null
}

function actionForCode(code: number | null, offset: number, limit: number): RatingAction {
  if (code === null || code > limit) return { kind: 'unknown' }
  if (code === 0) return { kind: 'external' }
  if (code === 1) return { kind: 'reject' }
  return { kind: 'tariff', row: offset + code }
}

export function previewNpa(input: string, value: ValueReader): RegionalRatingResult {
  if (!/^[2-9]\d{2}$/.test(input))
    return { action: { kind: 'unknown' }, source: 'Enter a three-digit area code from 200 to 999.' }
  const index = Number(input) - 200
  const section = `pair-${Math.floor(index / 2)}`
  const code = numeric(value(`${section}.${index % 2 === 0 ? 'even' : 'odd'}`), 15)
  return {
    action: actionForCode(code, 14, 15), source: `Area code ${input}`, section,
    note: code === 0 ? 'External rating is a candidate; the terminal can still apply ten-digit blocking.' : undefined,
  }
}

export function previewInternational(input: string, value: ValueReader): RegionalRatingResult {
  if (!/^\d{1,32}$/.test(input))
    return { action: { kind: 'unknown' }, source: 'Enter destination digits after the international access prefix.' }
  const slots = Array.from({ length: 200 }, (_, index) => ({
    index, prefix: numeric(value(`country-${index}.country`), 65535),
    code: numeric(value(`country-${index}.rate`), 255),
  }))
  if (slots.some(slot => slot.prefix === null))
    return { action: { kind: 'unknown' }, source: 'A destination prefix is unavailable or invalid.' }

  let matches: typeof slots = []
  let prefix = ''
  for (let width = Math.min(5, input.length); width > 0; width--) {
    const digits = input.slice(0, width)
    const candidate = Number(digits)
    if (!candidate) continue
    matches = slots.filter(slot => slot.prefix === candidate)
    if (matches.length) { prefix = digits; break }
  }
  const match = matches[0]
  const source = match ? `Destination ${match.index + 1}, prefix ${prefix}` : 'No destination matched'
  const section = match ? `country-${match.index}` : 'default'
  const duplicateSlots = matches.slice(1).map(slot => slot.index + 1)
  if (match && (match.code === null || match.code < 100))
    return { action: actionForCode(match.code, 28, 99), source, section, duplicateSlots }

  const mode = numeric(value('noMatch'), 3)
  const fallback = numeric(value('fallback'), 99)
  let action: RatingAction = { kind: 'unknown' }
  let note: string | undefined
  if (mode === 0) action = { kind: 'external' }
  else if (mode === 2 || mode === 3) action = { kind: 'reject' }
  else if (mode === 1) {
    // The matched high-code path selects the default as a tariff row even for
    // default codes 0 and 1, unlike the ordinary no-match path.
    action = match && fallback !== null ? { kind: 'tariff', row: fallback + 28 } : actionForCode(fallback, 28, 99)
    if (match && (fallback === 0 || fallback === 1))
      note = 'With a matched fallback code, this default selects a reserved tariff row instead of the direct no-match action. Review this combination.'
  }
  return { action, source, section, fallback: true, duplicateSlots, note }
}
