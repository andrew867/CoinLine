export type SelectionProfile = 'standard' | 'inmate'
export type DownloadSelectionPreferences = Record<SelectionProfile, number[] | null>

const storageKey = 'coinline.downloadSelectionPreferences.v1'

function validSelection(value: unknown): number[] | null {
  if (!Array.isArray(value) || value.length < 1 || value.length > 255 ||
      !value.every(number => Number.isInteger(number) && number >= 1 && number <= 255) ||
      new Set(value).size !== value.length) return null
  return [...value].sort((a, b) => a - b)
}

export function readDownloadSelectionPreferences(): DownloadSelectionPreferences {
  try {
    const parsed: unknown = JSON.parse(localStorage.getItem(storageKey) ?? 'null')
    if (parsed && typeof parsed === 'object') {
      const fields = parsed as Partial<DownloadSelectionPreferences>
      return { standard: validSelection(fields.standard), inmate: validSelection(fields.inmate) }
    }
  } catch {
    // The unavailable preference must not prevent manual selection.
  }
  return { standard: null, inmate: null }
}

export function saveDownloadSelectionPreference(profile: SelectionProfile, numbers: number[] | null): DownloadSelectionPreferences {
  const valid = numbers === null ? null : validSelection(numbers)
  if (numbers !== null && !valid) throw new Error('Choose at least one distinct valid record type.')
  const next = { ...readDownloadSelectionPreferences(), [profile]: valid }
  if (next.standard === null && next.inmate === null) localStorage.removeItem(storageKey)
  else localStorage.setItem(storageKey, JSON.stringify(next))
  return next
}
