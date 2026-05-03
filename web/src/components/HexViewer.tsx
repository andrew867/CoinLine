import { useMemo } from 'react'

export function formatHexSpaced(hex: string) {
  const clean = hex.replace(/\s/g, '')
  const parts: string[] = []
  for (let i = 0; i < clean.length; i += 2) {
    parts.push(clean.slice(i, i + 2))
  }
  return parts.join(' ')
}

type Props = {
  hex: string
  maxLines?: number
  'data-testid'?: string
}

export function HexViewer({ hex, maxLines, 'data-testid': testId = 'hex-viewer' }: Props) {
  const text = useMemo(() => {
    const spaced = formatHexSpaced(hex)
    if (!maxLines) return spaced
    const lines = spaced.match(/.{1,288}/g) ?? [spaced]
    return lines.slice(0, maxLines).join('\n')
  }, [hex, maxLines])

  return (
    <pre
      data-testid={testId}
      style={{
        fontFamily: 'ui-monospace, monospace',
        fontSize: 13,
        background: '#f6f8fa',
        padding: 12,
        overflow: 'auto',
        maxWidth: 'min(900px, 100%)',
        whiteSpace: 'pre-wrap',
        wordBreak: 'break-all',
      }}
    >
      {text}
    </pre>
  )
}
