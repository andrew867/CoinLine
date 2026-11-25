// The Western terminal character set uses a single display cell for these accents and symbols.
export const westernDisplayGlyphs = 'àáâäèéêëíîïçùúûüóôöñ¸¢◆█'
export const advertisingDisplayWidth = 20
export const glyphPalettes = {
  all: westernDisplayGlyphs,
  french: 'àâäèéêëîïçùûüôö¸¢',
  spanish: 'áéíóúüñ¢',
} as const

export function normalizedAdvertisingText(text: string) {
  return text.normalize('NFC')
}

export function authorableDisplayText(text: string, width: number) {
  const cells = Array.from(normalizedAdvertisingText(text))
  return cells.length <= width && cells.every(character =>
    (character >= ' ' && character <= '~') || westernDisplayGlyphs.includes(character))
}

export function authorableAdvertisingText(text: string) {
  return authorableDisplayText(text, advertisingDisplayWidth)
}
