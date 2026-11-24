import { useEffect, useMemo, useRef, useState } from 'react'
import {
  bitmapBit, bitmapByteIndex, bitmapHeight as height, bitmapPixelIsLit, bitmapWidth as width,
  emptyBitmap, encodeBitmap, exportBitmap, invertBitmap,
} from './graphicalBitmap'

export function NativeBitmapPreview({ name, value }: { name: string; value: string }) {
  const canvas = useRef<HTMLCanvasElement>(null)

  useEffect(() => {
    const context = canvas.current?.getContext('2d')
    if (!value || !context) return
    const bitmap = atob(value)
    const pixels = context.createImageData(width, height)
    for (let y = 0; y < height; y++) {
      for (let x = 0; x < width; x++) {
        const visible = bitmapPixelIsLit(bitmap, x, y)
        const index = (y * width + x) * 4
        pixels.data.set(visible ? [152, 247, 216, 255] : [22, 49, 42, 255], index)
      }
    }
    context.putImageData(pixels, 0, 0)
  }, [value])

  return <canvas ref={canvas} width={width} height={height} aria-label={`${name} preview`} role="img" />
}

export function NativeBitmapField({ name, value, savedValue, onChange }: {
  name: string; value: string | null; savedValue: string | null; onChange: (value: string) => void
}) {
  const [message, setMessage] = useState('')
  const [reading, setReading] = useState(false)
  const download = useMemo(() => value && exportBitmap(value), [value])

  async function replace(file: File) {
    setMessage('')
    setReading(true)
    try {
      if (file.size > 2_000_000) throw new Error('Choose an image smaller than 2 MB.')
      const image = await createImageBitmap(file)
      try {
        if (image.width !== width || image.height !== height) throw new Error('Choose a 112 × 72 pixel image.')
        const source = document.createElement('canvas')
        source.width = width
        source.height = height
        const context = source.getContext('2d')
        if (!context) throw new Error('The browser could not read this image.')
        context.fillStyle = '#ffffff'
        context.fillRect(0, 0, width, height)
        context.drawImage(image, 0, 0)
        const pixels = context.getImageData(0, 0, width, height).data
        const bitmap = new Uint8Array(width * height / 8)
        for (let y = 0; y < height; y++) {
          for (let x = 0; x < width; x++) {
            const index = (y * width + x) * 4
            const darkness = pixels[index] * .299 + pixels[index + 1] * .587 + pixels[index + 2] * .114
            if (darkness < 128) bitmap[bitmapByteIndex(x, y)] |= bitmapBit(y)
          }
        }
        onChange(encodeBitmap(bitmap))
      } finally {
        image.close()
      }
    } catch (error) {
      setMessage(error instanceof Error ? error.message : 'The browser could not open this image.')
    } finally {
      setReading(false)
    }
  }

  if (value === null) return <p className="page-intro">Artwork editing is available for bitmap screens.</p>
  return (
    <div className="native-table-bitmap">
      <NativeBitmapPreview name={name} value={value} />
      <label>{name}
        <input type="file" accept="image/png,image/bmp,image/jpeg" disabled={reading} onChange={(event) => {
          const file = event.target.files?.[0]
          event.target.value = ''
          if (file) void replace(file)
        }} />
      </label>
      <small>112 × 72 pixels. Dark pixels light up on the terminal.</small>
      <div className="row-actions">
        {download && <a href={download} download="screen-artwork.bmp">Download BMP</a>}
        <button type="button" disabled={reading} onClick={() => { setMessage(''); onChange(invertBitmap(value)) }}>Invert artwork</button>
        <button type="button" disabled={reading || value === emptyBitmap}
          onClick={() => { setMessage(''); onChange(emptyBitmap) }}>Clear artwork</button>
        {savedValue !== null && value !== savedValue && <button type="button" disabled={reading}
          onClick={() => { setMessage(''); onChange(savedValue) }}>Restore saved artwork</button>}
      </div>
      {message && <p role="status">{message}</p>}
    </div>
  )
}
