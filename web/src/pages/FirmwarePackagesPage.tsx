import { useCallback, useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { apiGet, apiPost } from '../api/client'

type Row = {
  id: string
  name: string
  versionLabel: string
  artifactSizeBytes: number
  primaryArtifactId: string | null
}

export function FirmwarePackagesPage() {
  const [rows, setRows] = useState<Row[]>([])
  const [loading, setLoading] = useState(true)
  const [err, setErr] = useState<string | null>(null)
  const [name, setName] = useState('')
  const [versionLabel, setVersionLabel] = useState('')
  const [checksumHex, setChecksumHex] = useState('')
  const [artifactSizeBytes, setArtifactSizeBytes] = useState('1')

  const refresh = useCallback(async () => {
    setErr(null)
    setLoading(true)
    try {
      const r = await apiGet<Row[]>('/api/firmware/packages')
      setRows(r)
    } catch (e) {
      setErr(e instanceof Error ? e.message : 'Failed to load packages — check API / network.')
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
      await apiPost<{ id: string }>('/api/firmware/packages', {
        name: name || 'Package',
        versionLabel: versionLabel || '0.0.0',
        checksumHex,
        artifactSizeBytes: Number(artifactSizeBytes) || 1,
        metadataJson: '{}',
      })
      setName('')
      setVersionLabel('')
      setChecksumHex('')
      await refresh()
    } catch (e) {
      setErr(e instanceof Error ? e.message : 'create failed')
    }
  }

  return (
    <div>
      <h1>Firmware packages</h1>
      <p style={{ maxWidth: 720, fontSize: 14, color: '#444' }}>
        Registry metadata only — artifact <code>ChecksumHex</code> must be a full SHA256 (64 hex characters).
        DLA/code-server wire behavior is <strong>HARDWARE_VALIDATION_REQUIRED</strong>.
      </p>

      {loading && <p>Loading…</p>}
      {err && <p style={{ color: 'crimson' }}>{err}</p>}

      <form onSubmit={onCreate} style={{ marginBottom: 24, maxWidth: 520 }}>
        <h2 style={{ fontSize: 16 }}>Register package</h2>
        <label style={{ display: 'block', marginBottom: 8 }}>
          Name{' '}
          <input value={name} onChange={(e) => setName(e.target.value)} style={{ width: '100%' }} />
        </label>
        <label style={{ display: 'block', marginBottom: 8 }}>
          Version label{' '}
          <input value={versionLabel} onChange={(e) => setVersionLabel(e.target.value)} style={{ width: '100%' }} />
        </label>
        <label style={{ display: 'block', marginBottom: 8 }}>
          SHA256 hex (64 chars){' '}
          <input
            value={checksumHex}
            onChange={(e) => setChecksumHex(e.target.value)}
            style={{ width: '100%', fontFamily: 'monospace', fontSize: 12 }}
            placeholder="64 hex characters"
            spellCheck={false}
          />
        </label>
        <label style={{ display: 'block', marginBottom: 8 }}>
          Artifact size (bytes){' '}
          <input
            type="number"
            min={1}
            value={artifactSizeBytes}
            onChange={(e) => setArtifactSizeBytes(e.target.value)}
            style={{ width: '100%' }}
          />
        </label>
        <button type="submit">Create package</button>
      </form>

      {!loading && !err && rows.length === 0 && (
        <p role="status" style={{ color: '#666' }}>
          No packages registered yet. Use the form above or seed data when the API is running.
        </p>
      )}

      {!loading && rows.length > 0 && (
        <table cellPadding={8} style={{ borderCollapse: 'collapse', width: '100%', maxWidth: 960 }}>
          <thead>
            <tr style={{ textAlign: 'left', borderBottom: '1px solid #ccc' }}>
              <th>Name</th>
              <th>Version</th>
              <th>Size</th>
              <th>Primary artifact</th>
            </tr>
          </thead>
          <tbody>
            {rows.map((r) => (
              <tr key={r.id} style={{ borderBottom: '1px solid #eee' }}>
                <td>
                  <Link to={`/firmware/packages/${r.id}`}>{r.name}</Link>
                </td>
                <td>{r.versionLabel}</td>
                <td>{r.artifactSizeBytes}</td>
                <td style={{ fontFamily: 'monospace', fontSize: 12 }}>{r.primaryArtifactId ?? '—'}</td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </div>
  )
}
