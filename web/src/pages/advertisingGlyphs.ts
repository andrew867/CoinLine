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

export function authorableAdvertisingText(text: string) {
  const cells = Array.from(normalizedAdvertisingText(text))
  return cells.length <= advertisingDisplayWidth && cells.every(character =>
    (character >= ' ' && character <= '~') || westernDisplayGlyphs.includes(character))
}
