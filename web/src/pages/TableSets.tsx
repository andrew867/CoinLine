import { useEffect, useState, type FormEvent } from 'react'
import { Link } from 'react-router-dom'
import { apiFetch } from '../api/client'
import { FirmwareVersionSelect } from '../components/FirmwareVersionSelect'


type Row = {
  id: string
  name: string
  customerId: string | null
  isDefault: boolean
  status: number
  publishedAtUtc: string | null
  publishGeneration: number
  targetFirmware: string | null
}

export function TableSets() {
  const [rows, setRows] = useState<Row[] | null>(null)
  const [err, setErr] = useState<string | null>(null)
  const [name, setName] = useState('')
  const [targetFirmwareVersionId, setTargetFirmwareVersionId] = useState('')

  function load() {
    apiFetch('/api/tables/sets')
      .then((r) => {
        if (!r.ok) throw new Error(String(r.status))
        return r.json() as Promise<Row[]>
      })
      .then(setRows)
      .catch((e) => setErr(e instanceof Error ? e.message : 'error'))
  }

  useEffect(() => {
    load()
  }, [])

  async function createSet(e: FormEvent) {
    e.preventDefault()
    const trimmed = name.trim()
    if (!trimmed) return
    setErr(null)
    const r = await apiFetch('/api/tables/sets', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ name: trimmed, customerId: null, isDefault: false, targetFirmwareVersionId: targetFirmwareVersionId || null }),
    })
    if (!r.ok) {
      setErr(String(r.status))
      return
    }
    setName('')
    load()
  }

  return (
    <div>
      <h1>Table sets</h1>
      <p><Link to="/inmate-features">Inmate Feature Configurations</Link></p>
      <form onSubmit={(e) => void createSet(e)} style={{ display: 'flex', flexWrap: 'wrap', gap: 8, alignItems: 'flex-end', marginBottom: 16 }}>
        <label>
          New set name
          <input value={name} onChange={(e) => setName(e.target.value)} required style={{ display: 'block' }} />
        </label>
        <FirmwareVersionSelect label="Target firmware" value={targetFirmwareVersionId}
          onChange={setTargetFirmwareVersionId} emptyLabel="No firmware target" />
        <button type="submit">Create table set</button>
      </form>
      {err && <p style={{ color: 'crimson' }}>{err}</p>}
      {!rows && <p>Loading…</p>}
      {rows && rows.length === 0 && <p style={{ color: '#666' }}>No table sets yet.</p>}
      {rows && rows.length > 0 && (
        <table cellPadding={8} style={{ borderCollapse: 'collapse' }}>
          <thead>
            <tr style={{ textAlign: 'left', borderBottom: '1px solid #ccc' }}>
              <th>Name</th>
              <th>Status</th>
              <th>Target firmware</th>
              <th>Default</th>
              <th>Gen</th>
            </tr>
          </thead>
          <tbody>
            {rows.map((r) => (
              <tr key={r.id} style={{ borderBottom: '1px solid #eee' }}>
                <td>
                  <Link to={`/table-sets/${r.id}`}>{r.name}</Link>
                </td>
                <td>{r.status === 1 ? 'Published' : 'Draft'}</td>
                <td>{r.targetFirmware ?? 'Any'}</td>
                <td>{r.isDefault ? 'yes' : ''}</td>
                <td>{r.publishGeneration}</td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </div>
  )
}
