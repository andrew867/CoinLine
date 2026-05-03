import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'

const headers = { 'X-Operator-Id': 'ui@local', 'X-Operator-Role': 'Admin' }

type Row = {
  id: string
  name: string
  customerId: string | null
  isDefault: boolean
  status: number
  publishedAtUtc: string | null
  publishGeneration: number
}

export function TableSets() {
  const [rows, setRows] = useState<Row[] | null>(null)
  const [err, setErr] = useState<string | null>(null)

  useEffect(() => {
    let c = false
    fetch('/api/tables/sets', { headers })
      .then((r) => {
        if (!r.ok) throw new Error(String(r.status))
        return r.json() as Promise<Row[]>
      })
      .then((j) => {
        if (!c) setRows(j)
      })
      .catch((e) => {
        if (!c) setErr(e instanceof Error ? e.message : 'error')
      })
    return () => {
      c = true
    }
  }, [])

  return (
    <div>
      <h1>Table sets</h1>
      {err && <p style={{ color: 'crimson' }}>{err}</p>}
      {!rows && <p>Loading…</p>}
      {rows && rows.length === 0 && <p style={{ color: '#666' }}>No table sets yet.</p>}
      {rows && rows.length > 0 && (
        <table cellPadding={8} style={{ borderCollapse: 'collapse' }}>
          <thead>
            <tr style={{ textAlign: 'left', borderBottom: '1px solid #ccc' }}>
              <th>Name</th>
              <th>Status</th>
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
