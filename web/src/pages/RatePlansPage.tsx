import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { apiGet } from '../api/client'

type Row = {
  id: string
  name: string
  mode: number
  customerId: string | null
  publishedVersionId: string | null
}

export function RatePlansPage() {
  const [rows, setRows] = useState<Row[]>([])
  const [loading, setLoading] = useState(true)
  const [err, setErr] = useState<string | null>(null)

  useEffect(() => {
    setLoading(true)
    setErr(null)
    apiGet<Row[]>('/api/rate-plans')
      .then(setRows)
      .catch((e) => setErr(e instanceof Error ? e.message : 'error'))
      .finally(() => setLoading(false))
  }, [])

  return (
    <div>
      <h1>Rate plans</h1>
      <p style={{ color: '#a60', maxWidth: 720 }}>
        MVP host-side rating is <strong>not production parity</strong> with reference terminal firmware until rules are UAT-backed and
        tested. Set-rated / table-rated paths require <code>HARDWARE_VALIDATION_REQUIRED</code>.
      </p>
      {loading && <p role="status">Loading rate plans…</p>}
      {!loading && err && <p style={{ color: 'crimson' }}>{err}</p>}
      {!loading && !err && rows.length === 0 && (
        <p role="status">
          No rate plans yet. Use the API (<code>POST /api/rate-plans</code>) or seed data to create one.
        </p>
      )}
      {!loading && !err && rows.length > 0 && (
        <table cellPadding={8} style={{ borderCollapse: 'collapse' }}>
          <thead>
            <tr style={{ borderBottom: '1px solid #ccc' }}>
              <th align="left">Name</th>
              <th align="left">Mode</th>
              <th align="left">Published version</th>
            </tr>
          </thead>
          <tbody>
            {rows.map((r) => (
              <tr key={r.id} style={{ borderBottom: '1px solid #eee' }}>
                <td>
                  <Link to={`/rate-plans/${r.id}`}>{r.name}</Link>
                </td>
                <td>{r.mode}</td>
                <td>{r.publishedVersionId ?? '—'}</td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </div>
  )
}
