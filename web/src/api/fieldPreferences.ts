export type FieldHighlight = { enabled: boolean; text: string; background: string }
export type FieldPreferences = { key: FieldHighlight; mandatory: FieldHighlight }

const storageKey = 'coinline.fieldPreferences'

export const defaultFieldPreferences: FieldPreferences = {
  key: { enabled: false, text: '#174463', background: '#e1f2ff' },
  mandatory: { enabled: false, text: '#563b06', background: '#fff1c7' },
}

function isColor(value: unknown): value is string {
  return typeof value === 'string' && /^#[0-9a-f]{6}$/i.test(value)
}

function normalizeHighlight(value: unknown, fallback: FieldHighlight): FieldHighlight {
  if (typeof value !== 'object' || value === null) return { ...fallback }
  const fields = value as Partial<FieldHighlight>
  return {
    enabled: typeof fields.enabled === 'boolean' ? fields.enabled : fallback.enabled,
    text: isColor(fields.text) ? fields.text : fallback.text,
    background: isColor(fields.background) ? fields.background : fallback.background,
  }
}

export function readFieldPreferences(): FieldPreferences {
  let saved: Partial<FieldPreferences> = {}
  try {
    const parsed: unknown = JSON.parse(localStorage.getItem(storageKey) ?? 'null')
    if (parsed && typeof parsed === 'object') saved = parsed as Partial<FieldPreferences>
  } catch {
    // An unavailable or invalid workstation preference must not prevent sign-in.
  }
  return {
    key: normalizeHighlight(saved.key, defaultFieldPreferences.key),
    mandatory: normalizeHighlight(saved.mandatory, defaultFieldPreferences.mandatory),
  }
}

export function applyFieldPreferences(preferences: FieldPreferences) {
  const root = document.documentElement
  for (const kind of ['key', 'mandatory'] as const) {
    const { enabled, text, background } = preferences[kind]
    root.setAttribute(`data-highlight-${kind}`, String(enabled))
    root.style.setProperty(`--field-${kind}-text`, text)
    root.style.setProperty(`--field-${kind}-background`, background)
  }
}

export function saveFieldPreferences(preferences: FieldPreferences) {
  localStorage.setItem(storageKey, JSON.stringify(preferences))
  applyFieldPreferences(preferences)
}
