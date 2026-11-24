import { useEffect, useState } from 'react'
import { apiGet } from '../api/client'

export function FirmwareVersionSelect({ label, value, onChange, emptyLabel, disabled = false }: {
  label: string
  value: string
  onChange: (value: string) => void
  emptyLabel: string
  disabled?: boolean
}) {
  const [versions, setVersions] = useState<{ id: string; label: string; buildId: string | null }[] | null>(null)
  const [error, setError] = useState(false)

  useEffect(() => {
    let current = true
    void apiGet<{ id: string; label: string; buildId: string | null }[]>('/api/firmware/versions')
      .then((result) => { if (current) setVersions(result) })
      .catch(() => { if (current) setError(true) })
    return () => { current = false }
  }, [])

  return (
    <label>
      {label}
      <select value={value} disabled={disabled || !versions} onChange={(event) => onChange(event.target.value)}
        style={{ display: 'block', maxWidth: '100%' }}>
        <option value="">{emptyLabel}</option>
        {value && !versions?.some((version) => version.id === value) &&
          <option value={value}>{versions ? 'Unavailable firmware version' : 'Loading version…'}</option>}
        {versions?.map((version) => (
          <option key={version.id} value={version.id}>
            {version.label}{version.buildId ? ` (${version.buildId})` : ''}
          </option>
        ))}
      </select>
      {error && <small role="alert">Could not load firmware versions.</small>}
    </label>
  )
}
