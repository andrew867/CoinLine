import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { apiPost } from '../api/client'

const headers = { 'X-Operator-Id': 'ui@local', 'X-Operator-Role': 'Admin' }

type Row = { id: string; name: string; tableNumber: number; description: string | null }

export function TableDefinitions() {
  const [rows, setRows] = useState<Row[] | null>(null)
  const [err, setErr] = useState<string | null>(null)
  const [name, setName] = useState('')
  const [num, setNum] = useState(100)
  const [desc, setDesc] = useState('')

  const load = () => {
    setErr(null)
    fetch('/api/tables/definitions', { headers })
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

  async function onCreate(e: React.FormEvent) {
    e.preventDefault()
    setErr(null)
    try {
      await apiPost<{ id: string }>('/api/tables/definitions', { name, tableNumber: num, description: desc || null })
      setName('')
      setDesc('')
      load()
    } catch (ex) {
      setErr(ex instanceof Error ? ex.message : 'error')
    }
  }

  return (
    <div>
      <h1>Table definitions</h1>
      <p style={{ color: '#555' }}>
        Host stores opaque binary payloads only — no firmware layout interpretation in this MVP.
      </p>
      {err && <p style={{ color: 'crimson' }}>{err}</p>}
      <form onSubmit={onCreate} style={{ marginBottom: 24, display: 'flex', flexWrap: 'wrap', gap: 8, alignItems: 'flex-end' }}>
        <label>
          Name
          <input value={name} onChange={(e) => setName(e.target.value)} required style={{ display: 'block' }} />
        </label>
        <label>
          Table #
          <input type="number" value={num} onChange={(e) => setNum(Number(e.target.value))} style={{ display: 'block', width: 80 }} />
        </label>
        <label>
          Description
          <input value={desc} onChange={(e) => setDesc(e.target.value)} style={{ display: 'block', width: 280 }} />
        </label>
        <button type="submit">Create definition</button>
      </form>
      {!rows && <p>Loading…</p>}
      {rows && rows.length === 0 && <p style={{ color: '#666' }}>No table definitions yet.</p>}
      {rows && rows.length > 0 && (
        <table cellPadding={8} style={{ borderCollapse: 'collapse' }}>
          <thead>
            <tr style={{ textAlign: 'left', borderBottom: '1px solid #ccc' }}>
              <th>Name</th>
              <th>#</th>
              <th>Id</th>
            </tr>
          </thead>
          <tbody>
            {rows.map((r) => (
              <tr key={r.id} style={{ borderBottom: '1px solid #eee' }}>
                <td>{r.name}</td>
                <td>{r.tableNumber}</td>
                <td>
                  <Link to={`/table-definitions/${r.id}`}>view</Link>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </div>
  )
}
