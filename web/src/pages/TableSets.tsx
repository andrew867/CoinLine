import { useEffect, useState, type FormEvent } from 'react'
import { Link } from 'react-router-dom'
import { apiFetch } from '../api/client'
import { canPerform, permissionOperation as op } from '../api/consoleSession'
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
  const [message, setMessage] = useState('')
  const canRead = canPerform('table-sets', op.read)
  const canAdd = canPerform('table-sets', op.add)

  function load() {
    if (!canRead) return
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
  }, [canRead])

  async function createSet(e: FormEvent) {
    e.preventDefault()
    const trimmed = name.trim()
    if (!canAdd || !trimmed) return
    setErr(null)
    setMessage('')
    try {
      const r = await apiFetch('/api/tables/sets', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ name: trimmed, customerId: null, isDefault: false, targetFirmwareVersionId: targetFirmwareVersionId || null }),
      })
      if (!r.ok) throw new Error((await r.json().catch(() => null))?.error ?? String(r.status))
      setName('')
      setMessage(`Created table set ${trimmed}.`)
      load()
    } catch (error) {
      setErr(error instanceof Error ? error.message : 'Could not create the table set.')
    }
  }

  return (
    <div>
      <h1>Table sets</h1>
      {canPerform('inmate-features', op.read) && <p><Link to="/inmate-features">Inmate Feature Configurations</Link></p>}
      {canAdd && <form onSubmit={(e) => void createSet(e)} style={{ display: 'flex', flexWrap: 'wrap', gap: 8, alignItems: 'flex-end', marginBottom: 16 }}>
        <label>
          New set name
          <input value={name} onChange={(e) => setName(e.target.value)} required style={{ display: 'block' }} />
        </label>
        <FirmwareVersionSelect label="Target firmware" value={targetFirmwareVersionId}
          onChange={setTargetFirmwareVersionId} emptyLabel="No firmware target" optionsPath="/api/tables/firmware-options" />
        <button type="submit">Create table set</button>
      </form>}
      {message && <p role="status">{message}</p>}
      {err && <p role="alert" style={{ color: 'crimson' }}>Table set request failed: {err}</p>}
      {canRead && !rows && !err && <p>Loading…</p>}
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
