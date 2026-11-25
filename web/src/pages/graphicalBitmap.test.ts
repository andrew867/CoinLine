import { expect, test } from 'vitest'
import { emptyBitmap, encodeBitmap, exportBitmap, invertBitmap } from './graphicalBitmap'

test('exports terminal columns as a bottom-up monochrome Windows bitmap', () => {
  const columns = new Uint8Array(1008)
  columns[0] = 1         // Top left.
  columns[113] = 2       // Column 1, row 9.
  columns[1007] = 128    // Bottom right.
  const url = exportBitmap(encodeBitmap(columns))
  expect(url).toMatch(/^data:image\/bmp;base64,/)
  const bytes = Uint8Array.from(atob(url.split(',')[1]), byte => byte.charCodeAt(0))
  const header = new DataView(bytes.buffer)
  expect(Array.from(bytes.slice(0, 2))).toEqual([0x42, 0x4d])
  expect(header.getUint32(2, true)).toBe(1214)
  expect(header.getUint32(10, true)).toBe(62)
  expect(header.getInt32(18, true)).toBe(112)
  expect(header.getInt32(22, true)).toBe(72)
  expect(header.getUint16(28, true)).toBe(1)
  expect(Array.from(bytes.slice(54, 62))).toEqual([0, 0, 0, 0, 255, 255, 255, 0])
  expect(Array.from(bytes.slice(62 + 13, 62 + 16))).toEqual([0xfe, 0, 0])
  expect(bytes[62 + (71 - 9) * 16]).toBe(0xbf)
  expect(bytes[62 + 71 * 16]).toBe(0x7f)
})

test('inverts every artwork byte and restores the original on a second invert', () => {
  const artwork = Uint8Array.from({ length: 1008 }, (_, index) => index % 256)
  const value = encodeBitmap(artwork)
  const inverted = invertBitmap(value)
  expect(atob(inverted)).toBe(String.fromCharCode(...artwork.map(byte => 255 - byte)))
  expect(invertBitmap(inverted)).toBe(value)
  expect(atob(emptyBitmap)).toBe('\0'.repeat(1008))
})
