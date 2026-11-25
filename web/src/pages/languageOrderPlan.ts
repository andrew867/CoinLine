export const languageOrderKeys = ['languageFirst', 'languageSecond', 'languageThird', 'languageFourth'] as const
const languageNames = ['Unused', 'English', 'French', 'Spanish', 'Japanese']
type ValueReader = (key: string) => string | null | undefined

function numeric(value: string | null | undefined) {
  return value != null && /^\d+$/.test(value) && Number(value) <= 15 ? Number(value) : null
}

export function languageOrderPlan(value: ValueReader) {
  const count = numeric(value('languageCount'))
  const validCount = count !== null && count >= 1 && count <= 4
  const rows = languageOrderKeys.map((key, index) => {
    const code = numeric(value(key))
    const active = validCount && index < count
    const supported = code !== null && code >= 1 && code <= 4
    return {
      key, position: index + 1, code, active, supported,
      name: code === null ? 'Unavailable' : languageNames[code] ?? `Stored code ${code}`,
      promptRecord: null as number | null,
    }
  })
  const enabled = rows.filter(row => row.active)
  const invalid = enabled.filter(row => !row.supported).map(row => row.position)
  const duplicate = enabled.some((row, index) => row.supported && enabled.slice(0, index).some(other => other.code === row.code))

  // On the inspected two-language installed profile, the selected language
  // codes determine the two visual table indices, not the scrolling order.
  // Do not guess a mapping for an invalid, duplicate or pre-install list.
  if (validCount && count <= 2 && invalid.length === 0 && !duplicate) {
    const ordered = [...enabled].sort((a, b) => a.code! - b.code!)
    ordered.forEach((row, index) => { row.promptRecord = 85 + index })
  }
  return { count, validCount, rows, invalid, duplicate, exceedsInstalledPair: validCount && count > 2 }
}

export function swapActiveLanguages(value: ValueReader, position: number, direction: -1 | 1): Record<string, string> | null {
  const plan = languageOrderPlan(value)
  const row = plan.rows[position - 1]
  const adjacent = plan.rows[position - 1 + direction]
  if (!row?.active || !adjacent?.active || !row.supported || !adjacent.supported || row.code === adjacent.code)
    return null
  return { [row.key]: String(adjacent.code), [adjacent.key]: String(row.code) }
}
