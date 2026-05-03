import { useCallback, useEffect, useState } from 'react'
import { apiGet, apiPost } from '../api/client'

type Row = { id: string; label: string; buildId: string | null }

export function FirmwareVersionsPage() {
  const [rows, setRows] = useState<Row[]>([])
  const [loading, setLoading] = useState(true)
  const [err, setErr] = useState<string | null>(null)
  const [label, setLabel] = useState('')
  const [buildId, setBuildId] = useState('')

  const refresh = useCallback(async () => {
    setErr(null)
    setLoading(true)
    try {
      setRows(await apiGet<Row[]>('/api/firmware/versions'))
    } catch (e) {
      setErr(e instanceof Error ? e.message : 'Failed to load versions.')
    } finally {
      setLoading(false)
    }
  }, [])

  useEffect(() => {
    void refresh()
  }, [refresh])

  async function onCreate(e: React.FormEvent) {
    e.preventDefault()
    setErr(null)
    try {
      await apiPost('/api/firmware/versions', {
        label: label || 'version',
        buildId: buildId || null,
        notes: 'ui',
      })
      setLabel('')
      setBuildId('')
      await refresh()
    } catch (e) {
      setErr(e instanceof Error ? e.message : 'create failed')
    }
  }

  return (
    <div>
      <h1>Firmware versions</h1>
      <p style={{ fontSize: 14, color: '#444', maxWidth: 640 }}>
        Lab catalog for compatibility rules — map to terminal <code>FirmwareVersionId</code>. HARDWARE_VALIDATION_REQUIRED for
        field parity.
      </p>
      {loading && <p role="status">Loading…</p>}
      {err && <p role="alert" style={{ color: 'crimson' }}>{err}</p>}
      <form onSubmit={onCreate} style={{ marginBottom: 20 }}>
        <input placeholder="Label" value={label} onChange={(e) => setLabel(e.target.value)} />{' '}
        <input placeholder="Build id" value={buildId} onChange={(e) => setBuildId(e.target.value)} />{' '}
        <button type="submit">Create</button>
      </form>
      {!loading && !err && rows.length === 0 && (
        <p role="status" style={{ color: '#666' }}>
          No firmware versions yet — create one or rely on seed data.
        </p>
      )}
      {!loading && rows.length > 0 && (
        <table cellPadding={8}>
          <thead>
            <tr style={{ textAlign: 'left' }}>
              <th>Label</th>
              <th>Build</th>
              <th>Id</th>
            </tr>
          </thead>
          <tbody>
            {rows.map((r) => (
              <tr key={r.id}>
                <td>{r.label}</td>
                <td>{r.buildId ?? '—'}</td>
                <td style={{ fontFamily: 'monospace', fontSize: 11 }}>{r.id}</td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </div>
  )
}
