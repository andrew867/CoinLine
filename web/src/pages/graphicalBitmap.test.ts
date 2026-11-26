import { expect, test } from 'vitest'
import { readFileSync } from 'node:fs'
import { createHash } from 'node:crypto'
import { dirname, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'
import { bitmapPixelIsLit, emptyBitmap, encodeBitmap, exportBitmap, invertBitmap, packBitmapPixels } from './graphicalBitmap'

test('exports terminal columns as a bottom-up monochrome Windows bitmap', () => {
  const columns = new Uint8Array(1008)
  columns[896] = 1       // Top left, last band.
  columns[785] = 2       // Column 1, row 9.
  columns[111] = 128     // Bottom right, first band.
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

test('packs and reads asymmetric points across the bottom-first band boundaries', () => {
  const points = new Set(['0,0', '1,7', '2,8', '3,63', '4,64', '111,71'])
  const data = packBitmapPixels((x, y) => points.has(`${x},${y}`))
  const bytes = Uint8Array.from(atob(data), ch => ch.charCodeAt(0))
  const expected = new Uint8Array(1008)
  expected[896] = 1; expected[897] = 128; expected[786] = 1
  expected[115] = 128; expected[4] = 1; expected[111] = 128
  expect(bytes).toEqual(expected)
  for (let y = 0; y < 72; y++) for (let x = 0; x < 112; x++)
    expect(bitmapPixelIsLit(atob(data), x, y)).toBe(points.has(`${x},${y}`))
})

// SHA-256 of the 112x72 row-major illuminated-pixel masks from the matching
// starter PNGs. These asymmetric independent image references catch a band
// order error even if the same wrong orientation is used by import and export.
const starterMasks = [
  '918bd2a3ac143948814e04f2ebf728ca9ca36bce36d1ace04d0a7c578e04503f',
  '74f6d65091216d80bba2eb51b39a1d07977508f676180e8e2931992f288146f1',
  '4b49ef0176972e0817e5feb28248f539a2c4e2d96a209ed928cb5c4b55b6dd78',
  'fa86e02c71906af7495430ef7ad16ffb44d28f83eb78ad8f879e9addb8c05961',
  'c2388916299e0b3ade7408b042a197b89ba3843a6865f8ecbf9b3e045d9fd888',
  '6320537ebfee6c3090dd45af5b1c051395e51677609db40aa331b4d7174a6130',
  '264ffb2cf018c7ee3329566e4d66c18e778904c71f8945e77743ed65df8711dd',
  '1c7ab5f95cfd809b5b95be037088394ba4a5a9c85c0cefa6a9cd383196a64b42',
  '88efc72a71cbbf4765c22e16d5c802a42b6e60a4ec27816b91ebaa3d5d844991',
  '1adb1a6cc568d13611d739ea727414edec222b7adad68168cfd04ce76e018275',
  '47f88a07a5d5d910ccab0d9f9384591b987ad4b7c0bbcba0c1c3f6a5b35343bc',
  'd5753e1525754c33f9a249416ac53aed313331f6aecb87856eabeb45624aafbb',
  '0ec7150abd6042f8b0eb7da9ba3f47a291c879b2e07068316db39582a9e41e02',
  '783c6a482042f3613ec2adc193f02e07aef0fd62ef04cd395836f83413b96311',
  '0136a21dcde3a1dbcb781e686fafac1375faa10657526bdc1767726a57bbc24e',
  '124d8fe661125b0662ea887eaa830dd61c54e23ac87c9864186dbe75715a364a',
  '242f3cf2515146d8e8076559f30f2e44f1ebd99e5ec37de4e71d37e6cc087237',
  '5e01bf2f185ef591ea1a6fa1f042b2b78665c914b0a686ec5f1e17df4e1dd8ea',
  '6df90b0b479b8116360b6cc19076ad53ade5bdf0fc8fc987f522a82aa1551b48',
  '116037b3fc68d7a3b65ff9a63e8bac501c155f41e2ca9d85c1de06820befb7b3',
]

test('all twenty packed starter images match their independently rendered pixel masks', () => {
  const root = resolve(dirname(fileURLToPath(import.meta.url)), '../../../backend/src/HostPlatform.Protocols.Tables/Assets/GraphicalStarter')
  for (let frame = 0; frame < starterMasks.length; frame++) {
    const raw = readFileSync(resolve(root, `frame-${String(frame + 1).padStart(2, '0')}.bin`))
    expect(raw.length).toBe(1008)
    const image = raw.toString('latin1')
    const pixels = Uint8Array.from({ length: 112 * 72 }, (_, i) => Number(bitmapPixelIsLit(image, i % 112, Math.floor(i / 112))))
    expect(createHash('sha256').update(pixels).digest('hex'), `starter frame ${frame + 1}`).toBe(starterMasks[frame])
    expect(packBitmapPixels((x, y) => Boolean(pixels[y * 112 + x]))).toBe(raw.toString('base64'))
  }
})

test('inverts every artwork byte and restores the original on a second invert', () => {
  const artwork = Uint8Array.from({ length: 1008 }, (_, index) => index % 256)
  const value = encodeBitmap(artwork)
  const inverted = invertBitmap(value)
  expect(atob(inverted)).toBe(String.fromCharCode(...artwork.map(byte => 255 - byte)))
  expect(invertBitmap(inverted)).toBe(value)
  expect(atob(emptyBitmap)).toBe('\0'.repeat(1008))
})
