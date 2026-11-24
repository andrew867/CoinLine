import { useEffect, useState } from 'react'
import { apiGet } from '../api/client'

type TerminalFamily = { id: string; release: string; variant: string; maximizerTypeNumbers: number[] }

export function TerminalFamilySelect({ value, onChange, disabled = false, emptyLabel = 'Unknown family' }: {
  value: string
  onChange: (value: string) => void
  disabled?: boolean
  emptyLabel?: string
}) {
  const [families, setFamilies] = useState<TerminalFamily[] | null>(null)
  const [error, setError] = useState(false)

  useEffect(() => {
    let current = true
    void apiGet<TerminalFamily[]>('/api/terminals/families')
      .then((result) => { if (current) setFamilies(result) })
      .catch(() => { if (current) setError(true) })
    return () => { current = false }
  }, [])

  const groups = new Map<string, TerminalFamily[]>()
  for (const family of families ?? []) {
    const group = groups.get(family.release)
    if (group) group.push(family)
    else groups.set(family.release, [family])
  }

  return (
    <label>
      Terminal family
      <select value={value} disabled={disabled || !families} onChange={(event) => onChange(event.target.value)}
        style={{ display: 'block', maxWidth: '100%' }}>
        <option value="">{emptyLabel}</option>
        {value && !families?.some((family) => family.id === value) &&
          <option value={value}>{families ? 'Unavailable terminal family' : 'Loading family…'}</option>}
        {[...groups].map(([release, options]) => <optgroup key={release} label={release}>
          {options.map((family) => <option key={family.id} value={family.id}>
            {release} {family.variant} ({family.maximizerTypeNumbers.join('/')})
          </option>)}
        </optgroup>)}
      </select>
      {error && <small role="alert">Could not load terminal families.</small>}
    </label>
  )
}
