export const bitmapWidth = 112
export const bitmapHeight = 72
const bitmapBytes = bitmapWidth * bitmapHeight / 8

// The packed graphic stores the bottom display band first. Within each 8-dot
// band, bit 0 is the top pixel and bit 7 is the bottom pixel.
export const bitmapByteIndex = (x: number, y: number) => Math.floor((bitmapHeight - 1 - y) / 8) * bitmapWidth + x
export const bitmapBit = (y: number) => 1 << (y % 8)

export const bitmapPixelIsLit = (bitmap: string, x: number, y: number) =>
  (bitmap.charCodeAt(bitmapByteIndex(x, y)) & bitmapBit(y)) !== 0

export const encodeBitmap = (bitmap: Uint8Array) => btoa(String.fromCharCode(...bitmap))
export const emptyBitmap = encodeBitmap(new Uint8Array(bitmapBytes))

export function packBitmapPixels(isLit: (x: number, y: number) => boolean): string {
  const bitmap = new Uint8Array(bitmapBytes)
  for (let y = 0; y < bitmapHeight; y++) {
    for (let x = 0; x < bitmapWidth; x++) {
      if (isLit(x, y)) bitmap[bitmapByteIndex(x, y)] |= bitmapBit(y)
    }
  }
  return encodeBitmap(bitmap)
}

export function invertBitmap(value: string): string {
  return encodeBitmap(Uint8Array.from(atob(value), byte => byte.charCodeAt(0) ^ 255))
}

export function exportBitmap(value: string): string {
  const bitmap = atob(value)
  const pixelsOffset = 14 + 40 + 8
  const rowBytes = Math.ceil(bitmapWidth / 32) * 4
  const pixelsSize = rowBytes * bitmapHeight
  const file = new Uint8Array(pixelsOffset + pixelsSize)
  const header = new DataView(file.buffer)
  file.set([0x42, 0x4d])
  header.setUint32(2, file.length, true)
  header.setUint32(10, pixelsOffset, true)
  header.setUint32(14, 40, true)
  header.setInt32(18, bitmapWidth, true)
  header.setInt32(22, bitmapHeight, true)
  header.setUint16(26, 1, true)
  header.setUint16(28, 1, true)
  header.setUint32(34, pixelsSize, true)
  file.set([255, 255, 255], 58) // Palette index 0 is black, index 1 is white.
  for (let y = 0; y < bitmapHeight; y++) {
    const row = pixelsOffset + (bitmapHeight - 1 - y) * rowBytes
    for (let x = 0; x < bitmapWidth; x++) {
      if (!bitmapPixelIsLit(bitmap, x, y)) file[row + Math.floor(x / 8)] |= 0x80 >> (x % 8)
    }
  }
  return `data:image/bmp;base64,${encodeBitmap(file)}`
}
